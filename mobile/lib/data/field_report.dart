/// Estado de un reporte en el teléfono.
/// Un reporte solo deja de estar pendiente cuando el sistema central confirma que lo tiene.
enum ReportStatus {
  /// Guardado en el teléfono; falta enviarlo o el último envío falló por conexión.
  pending('PENDING', 'Pendiente'),

  /// El sistema central confirmó que lo tiene (INSERTED o DUPLICATE).
  synced('SYNCED', 'Sincronizado'),

  /// Ya existe en el sistema central con el mismo id y otro contenido. Requiere decisión.
  conflict('CONFLICT', 'En conflicto'),

  /// Incumple una regla del sistema central. Requiere corrección.
  rejected('REJECTED', 'Rechazado');

  const ReportStatus(this.code, this.label);

  final String code;
  final String label;

  bool get needsAttention => this == conflict || this == rejected;

  static ReportStatus fromCode(String code) =>
      values.firstWhere((s) => s.code == code, orElse: () => pending);
}

/// De dónde salió la coordenada.
enum LocationSource {
  gps('GPS', 'GPS del teléfono'),
  map('MAP', 'Punto elegido en el mapa'),
  manual('MANUAL', 'Escrita a mano');

  const LocationSource(this.code, this.label);

  final String code;
  final String label;

  static LocationSource fromCode(String code) =>
      values.firstWhere((s) => s.code == code, orElse: () => manual);
}

/// Reporte de afectación capturado en campo.
/// [clientId] lo genera el teléfono (UUID v7) y es el identificador definitivo en el sistema central,
/// así que reenviar un reporte nunca lo duplica.
class FieldReport {
  const FieldReport({
    required this.clientId,
    required this.eventId,
    required this.eventName,
    required this.latitude,
    required this.longitude,
    required this.locationSource,
    required this.affectationLevel,
    required this.capturedAt,
    this.accuracyMeters,
    this.affectedPeople = 0,
    this.municipality = '',
    this.department = '',
    this.description = '',
    this.status = ReportStatus.pending,
    this.attempts = 0,
    this.lastError,
    this.serverReason,
    this.syncedAt,
    this.nextAttemptAt,
  });

  final String clientId;
  final String eventId;
  final String eventName;
  final double latitude;
  final double longitude;
  final double? accuracyMeters;
  final LocationSource locationSource;
  final int affectationLevel;
  final int affectedPeople;
  final String municipality;
  final String department;
  final String description;

  /// Hora de captura en UTC: cuándo ocurrió la observación (no cuándo llegó al sistema central).
  final DateTime capturedAt;

  final ReportStatus status;
  final int attempts;

  /// Último error de conexión al intentar enviarlo.
  final String? lastError;

  /// Motivo del sistema central cuando lo rechazó o encontró un conflicto.
  final String? serverReason;
  final DateTime? syncedAt;

  /// No se reintenta antes de esta hora (espera creciente tras fallos de conexión).
  final DateTime? nextAttemptAt;

  FieldReport copyWith({
    String? clientId,
    String? eventId,
    String? eventName,
    double? latitude,
    double? longitude,
    double? Function()? accuracyMeters,
    LocationSource? locationSource,
    int? affectationLevel,
    int? affectedPeople,
    String? municipality,
    String? department,
    String? description,
    DateTime? capturedAt,
    ReportStatus? status,
    int? attempts,
    String? Function()? lastError,
    String? Function()? serverReason,
    DateTime? Function()? syncedAt,
    DateTime? Function()? nextAttemptAt,
  }) =>
      FieldReport(
        clientId: clientId ?? this.clientId,
        eventId: eventId ?? this.eventId,
        eventName: eventName ?? this.eventName,
        latitude: latitude ?? this.latitude,
        longitude: longitude ?? this.longitude,
        accuracyMeters: accuracyMeters != null ? accuracyMeters() : this.accuracyMeters,
        locationSource: locationSource ?? this.locationSource,
        affectationLevel: affectationLevel ?? this.affectationLevel,
        affectedPeople: affectedPeople ?? this.affectedPeople,
        municipality: municipality ?? this.municipality,
        department: department ?? this.department,
        description: description ?? this.description,
        capturedAt: capturedAt ?? this.capturedAt,
        status: status ?? this.status,
        attempts: attempts ?? this.attempts,
        lastError: lastError != null ? lastError() : this.lastError,
        serverReason: serverReason != null ? serverReason() : this.serverReason,
        syncedAt: syncedAt != null ? syncedAt() : this.syncedAt,
        nextAttemptAt: nextAttemptAt != null ? nextAttemptAt() : this.nextAttemptAt,
      );

  Map<String, Object?> toRow() => {
        'client_id': clientId,
        'event_id': eventId,
        'event_name': eventName,
        'latitude': latitude,
        'longitude': longitude,
        'accuracy_m': accuracyMeters,
        'location_source': locationSource.code,
        'affectation_level': affectationLevel,
        'affected_people': affectedPeople,
        'municipality': municipality,
        'department': department,
        'description': description,
        'captured_at': capturedAt.toUtc().toIso8601String(),
        'status': status.code,
        'attempts': attempts,
        'last_error': lastError,
        'server_reason': serverReason,
        'synced_at': syncedAt?.toUtc().toIso8601String(),
        'next_attempt_at': nextAttemptAt?.toUtc().toIso8601String(),
      };

  factory FieldReport.fromRow(Map<String, Object?> row) => FieldReport(
        clientId: row['client_id'] as String,
        eventId: row['event_id'] as String,
        eventName: row['event_name'] as String,
        latitude: (row['latitude'] as num).toDouble(),
        longitude: (row['longitude'] as num).toDouble(),
        accuracyMeters: (row['accuracy_m'] as num?)?.toDouble(),
        locationSource: LocationSource.fromCode(row['location_source'] as String),
        affectationLevel: row['affectation_level'] as int,
        affectedPeople: row['affected_people'] as int,
        municipality: row['municipality'] as String,
        department: row['department'] as String,
        description: row['description'] as String,
        capturedAt: DateTime.parse(row['captured_at'] as String),
        status: ReportStatus.fromCode(row['status'] as String),
        attempts: row['attempts'] as int,
        lastError: row['last_error'] as String?,
        serverReason: row['server_reason'] as String?,
        syncedAt: _date(row['synced_at']),
        nextAttemptAt: _date(row['next_attempt_at']),
      );

  /// Registro del contrato POST /api/v1/sync/batch.
  Map<String, Object?> toSyncJson() => {
        'clientId': clientId,
        'eventId': eventId,
        'latitude': latitude,
        'longitude': longitude,
        'affectationLevel': affectationLevel,
        'affectedPeople': affectedPeople,
        'municipality': municipality.isEmpty ? null : municipality,
        'department': department.isEmpty ? null : department,
        'description': description.isEmpty ? null : description,
        'reportedAt': capturedAt.toUtc().toIso8601String(),
      };

  static DateTime? _date(Object? value) => value is String ? DateTime.parse(value) : null;
}
