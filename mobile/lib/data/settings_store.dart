import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

/// Configuración del dispositivo, guardada en el teléfono.
class SettingsStore {
  SettingsStore(this._prefs);

  final SharedPreferences _prefs;

  /// 10.0.2.2 es la máquina anfitriona vista desde el emulador de Android.
  static const String defaultServerUrl = 'http://10.0.2.2:5002';
  static const int defaultBatchSize = 100;
  static const List<int> batchSizes = [25, 50, 100, 200, 500];

  static const _serverUrl = 'serverUrl';
  static const _deviceId = 'deviceId';
  static const _autoSync = 'autoSync';
  static const _batchSize = 'batchSize';
  static const _lastSyncAt = 'lastSyncAt';
  static const _lastSyncSummary = 'lastSyncSummary';

  static Future<SettingsStore> load() async => SettingsStore(await SharedPreferences.getInstance());

  String get serverUrl => _prefs.getString(_serverUrl) ?? defaultServerUrl;
  Future<void> setServerUrl(String value) => _prefs.setString(_serverUrl, normalizeUrl(value));

  /// Identificador estable del teléfono; se genera la primera vez.
  String get deviceId {
    final existing = _prefs.getString(_deviceId);
    if (existing != null) return existing;
    final created = 'android-${const Uuid().v4().substring(0, 13)}';
    _prefs.setString(_deviceId, created);
    return created;
  }

  bool get autoSync => _prefs.getBool(_autoSync) ?? true;
  Future<void> setAutoSync(bool value) => _prefs.setBool(_autoSync, value);

  int get batchSize => _prefs.getInt(_batchSize) ?? defaultBatchSize;
  Future<void> setBatchSize(int value) => _prefs.setInt(_batchSize, value);

  DateTime? get lastSyncAt {
    final value = _prefs.getString(_lastSyncAt);
    return value == null ? null : DateTime.tryParse(value);
  }

  String? get lastSyncSummary => _prefs.getString(_lastSyncSummary);

  Future<void> setLastSync(DateTime at, String summary) async {
    await _prefs.setString(_lastSyncAt, at.toUtc().toIso8601String());
    await _prefs.setString(_lastSyncSummary, summary);
  }

  /// Quita espacios y la barra final; agrega http:// si falta.
  static String normalizeUrl(String value) {
    var url = value.trim();
    while (url.endsWith('/')) {
      url = url.substring(0, url.length - 1);
    }
    if (!url.startsWith('http://') && !url.startsWith('https://')) url = 'http://$url';
    return url;
  }
}
