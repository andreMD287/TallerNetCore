import 'dart:math';

import 'package:uuid/uuid.dart';

import '../data/field_report.dart';
import '../data/report_repository.dart';
import 'disaster_api.dart';

/// Resumen de un intento de sincronización.
class SyncOutcome {
  const SyncOutcome({
    this.sent = 0,
    this.synced = 0,
    this.conflicts = 0,
    this.rejected = 0,
    this.error,
    this.skipped = false,
  });

  /// Ya había una sincronización en curso.
  const SyncOutcome.busy() : this(skipped: true);

  final int sent;
  final int synced;
  final int conflicts;
  final int rejected;

  /// Error que detuvo el envío; los reportes no enviados siguen pendientes.
  final String? error;
  final bool skipped;

  bool get failed => error != null;

  String get summary {
    if (skipped) return 'Ya hay una sincronización en curso.';
    if (sent == 0 && error == null) return 'No hay reportes pendientes.';
    final parts = <String>[
      '$synced sincronizados',
      if (conflicts > 0) '$conflicts en conflicto',
      if (rejected > 0) '$rejected rechazados',
    ];
    return error == null ? parts.join(', ') : '${parts.join(', ')}. Se detuvo: $error';
  }
}

/// Envía los reportes pendientes al sistema central en lotes de [batchSize].
///
/// - Cada reporte conserva el id que le dio el teléfono, así que reenviar es seguro: si el servidor
///   ya lo tenía, responde DUPLICATE y se marca como sincronizado.
/// - Si falla la conexión, los reportes siguen pendientes y esperan cada vez más antes de reintentar
///   (30 s, 1 min, 2 min… hasta 15 min). Nada se borra del teléfono.
/// - CONFLICT y REJECTED quedan para revisión y no se reintentan solos.
class SyncEngine {
  SyncEngine({
    required this.reports,
    required this.apiFor,
    DateTime Function()? clock,
    Uuid? uuid,
  })  : _clock = clock ?? DateTime.now,
        _uuid = uuid ?? const Uuid();

  final ReportRepository reports;
  final DisasterApi Function() apiFor;
  final DateTime Function() _clock;
  final Uuid _uuid;
  bool _running = false;

  bool get isRunning => _running;

  static const Duration _firstWait = Duration(seconds: 30);
  static const Duration _maxWait = Duration(minutes: 15);

  /// Espera antes del siguiente intento según cuántos han fallado.
  static Duration backoff(int attempts) {
    final seconds = _firstWait.inSeconds * pow(2, max(0, attempts - 1));
    return Duration(seconds: min(seconds.toInt(), _maxWait.inSeconds));
  }

  /// [force] ignora la espera entre reintentos (botón "Sincronizar ahora").
  Future<SyncOutcome> run({required String deviceId, required int batchSize, bool force = false}) async {
    if (_running) return const SyncOutcome.busy();
    _running = true;
    var sent = 0, synced = 0, conflicts = 0, rejected = 0;
    try {
      final api = apiFor();
      final attempted = <String>{};
      while (true) {
        final due = (await reports.pendingDue(now: force ? null : _clock(), limit: batchSize))
            .where((r) => !attempted.contains(r.clientId))
            .toList();
        if (due.isEmpty) break;
        attempted.addAll(due.map((r) => r.clientId));

        final SyncResponse response;
        try {
          response = await api.syncBatch(batchId: _uuid.v7(), deviceId: deviceId, reports: due);
        } on ApiException catch (e) {
          final now = _clock();
          await reports.markFailure(due, e.message, (attempts) => now.add(backoff(attempts)));
          return SyncOutcome(sent: sent, synced: synced, conflicts: conflicts, rejected: rejected, error: e.message);
        }

        final updates = <String, ({ReportStatus status, String? reason})>{};
        for (final result in response.results) {
          final status = switch (result.status) {
            'INSERTED' || 'DUPLICATE' => ReportStatus.synced,
            'CONFLICT' => ReportStatus.conflict,
            _ => ReportStatus.rejected,
          };
          updates[result.clientId] = (status: status, reason: status == ReportStatus.synced ? null : result.reason);
          switch (status) {
            case ReportStatus.synced:
              synced++;
            case ReportStatus.conflict:
              conflicts++;
            case ReportStatus.rejected:
              rejected++;
            case ReportStatus.pending:
              break;
          }
        }
        await reports.applyResults(updates, _clock());
        sent += due.length;
      }
      return SyncOutcome(sent: sent, synced: synced, conflicts: conflicts, rejected: rejected);
    } finally {
      _running = false;
    }
  }
}
