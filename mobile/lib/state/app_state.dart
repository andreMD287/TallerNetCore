import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/widgets.dart';
import 'package:uuid/uuid.dart';

import '../data/disaster_event.dart';
import '../data/field_report.dart';
import '../data/report_repository.dart';
import '../data/settings_store.dart';
import '../sync/disaster_api.dart';
import '../sync/sync_engine.dart';

/// Estado de la conexión tal como lo ve el usuario.
enum LinkStatus {
  /// El teléfono no tiene red.
  offline,

  /// Hay red, pero el sistema central no responde o no está disponible.
  serverUnavailable,

  /// Hay red y el sistema central responde.
  online,

  /// Todavía no se ha comprobado.
  unknown,
}

/// Estado compartido de la app. Guarda primero en el teléfono y sincroniza cuando puede:
/// al recuperar la red, al volver a la app, cada minuto mientras está abierta y al guardar un reporte.
class AppState extends ChangeNotifier with WidgetsBindingObserver {
  AppState({
    required this.reports,
    required this.events,
    required this.settings,
    Connectivity? connectivity,
  }) : _connectivity = connectivity ?? Connectivity() {
    engine = SyncEngine(reports: reports, apiFor: () => DisasterApi(settings.serverUrl));
  }

  final ReportRepository reports;
  final EventRepository events;
  final SettingsStore settings;
  final Connectivity _connectivity;
  late final SyncEngine engine;

  static const Duration periodicSync = Duration(minutes: 1);

  StreamSubscription<List<ConnectivityResult>>? _connectivitySub;
  Timer? _timer;

  LinkStatus connection = LinkStatus.unknown;
  ReportCounts counts = const ReportCounts();
  List<DisasterEvent> eventList = const [];
  DateTime? eventsDownloadedAt;
  bool syncing = false;
  SyncOutcome? lastOutcome;

  /// Se incrementa cada vez que cambian los reportes, para que las listas se recarguen.
  int revision = 0;

  Future<void> start() async {
    WidgetsBinding.instance.addObserver(this);
    await refresh();
    final initial = await _connectivity.checkConnectivity();
    await _onConnectivity(initial);
    _connectivitySub = _connectivity.onConnectivityChanged.listen(_onConnectivity);
    _timer = Timer.periodic(periodicSync, (_) => autoSync());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) autoSync();
  }

  Future<void> _onConnectivity(List<ConnectivityResult> results) async {
    final hasNetwork = results.any((r) => r != ConnectivityResult.none);
    if (!hasNetwork) {
      _setConnection(LinkStatus.offline);
      return;
    }
    // Volvió la red: se comprueba el servidor y se envía lo pendiente.
    await checkServer();
    if (eventList.isEmpty && connection == LinkStatus.online) await downloadEvents();
    await autoSync();
  }

  void _setConnection(LinkStatus value) {
    if (connection == value) return;
    connection = value;
    notifyListeners();
  }

  Future<bool> checkServer() async {
    final api = DisasterApi(settings.serverUrl);
    final ok = await api.isAvailable();
    api.close();
    final results = await _connectivity.checkConnectivity();
    final hasNetwork = results.any((r) => r != ConnectivityResult.none);
    _setConnection(!hasNetwork ? LinkStatus.offline : ok ? LinkStatus.online : LinkStatus.serverUnavailable);
    return ok;
  }

  Future<void> refresh() async {
    counts = await reports.counts();
    eventList = await events.all();
    eventsDownloadedAt = await events.lastDownload();
    revision++;
    notifyListeners();
  }

  /// Sincronización automática: solo si está activada, hay red y hay algo pendiente.
  Future<void> autoSync() async {
    if (!settings.autoSync || connection == LinkStatus.offline || counts.pending == 0) return;
    await syncNow();
  }

  /// [force] ignora la espera entre reintentos.
  Future<SyncOutcome> syncNow({bool force = false}) async {
    if (syncing) return const SyncOutcome.busy();
    syncing = true;
    notifyListeners();
    try {
      final outcome = await engine.run(deviceId: settings.deviceId, batchSize: settings.batchSize, force: force);
      if (!outcome.skipped) {
        lastOutcome = outcome;
        if (outcome.sent > 0 || outcome.failed) await settings.setLastSync(DateTime.now(), outcome.summary);
        if (outcome.failed) {
          await checkServer();
        } else if (outcome.sent > 0) {
          _setConnection(LinkStatus.online);
        }
      }
      return outcome;
    } finally {
      syncing = false;
      await refresh();
    }
  }

  /// Descarga los eventos activos para poder reportar sin conexión.
  Future<String?> downloadEvents() async {
    final api = DisasterApi(settings.serverUrl);
    try {
      final list = await api.fetchEvents();
      await events.replaceAll(list, DateTime.now());
      await refresh();
      return null;
    } on ApiException catch (e) {
      return e.message;
    } finally {
      api.close();
    }
  }

  /// Guarda un reporte nuevo o corregido. Siempre queda en el teléfono antes de cualquier envío.
  Future<void> saveReport(FieldReport report) async {
    await reports.save(report.copyWith(
      status: ReportStatus.pending,
      attempts: 0,
      lastError: () => null,
      serverReason: () => null,
      nextAttemptAt: () => null,
      syncedAt: () => null,
    ));
    await refresh();
    unawaited(autoSync());
  }

  /// Un reporte en conflicto se puede reenviar como reporte nuevo (otro id); el original no se toca.
  Future<void> resendAsNew(FieldReport report) async {
    await reports.delete(report.clientId);
    await saveReport(report.copyWith(clientId: const Uuid().v7()));
  }

  Future<void> deleteReport(String clientId) async {
    await reports.delete(clientId);
    await refresh();
  }

  Future<int> clearSynced() async {
    final removed = await reports.deleteSynced();
    await refresh();
    return removed;
  }

  Future<void> updateServerUrl(String url) async {
    await settings.setServerUrl(url);
    await checkServer();
  }

  Future<void> updateAutoSync(bool value) async {
    await settings.setAutoSync(value);
    notifyListeners();
    if (value) await autoSync();
  }

  Future<void> updateBatchSize(int value) async {
    await settings.setBatchSize(value);
    notifyListeners();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _connectivitySub?.cancel();
    _timer?.cancel();
    super.dispose();
  }
}
