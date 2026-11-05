/// Evento de desastre descargado del sistema central para poder reportar sin conexión (RF-ED-11).
class DisasterEvent {
  const DisasterEvent({
    required this.id,
    required this.name,
    required this.type,
    this.startedAt,
  });

  final String id;
  final String name;
  final String type;
  final DateTime? startedAt;

  static const Map<String, String> _typeLabels = {
    'FLOOD': 'Inundación',
    'LANDSLIDE': 'Deslizamiento',
    'EARTHQUAKE': 'Sismo',
    'WILDFIRE': 'Incendio forestal',
    'WINDSTORM': 'Vendaval',
    'OTHER': 'Otro',
  };

  String get typeLabel => _typeLabels[type] ?? type;

  factory DisasterEvent.fromJson(Map<String, dynamic> json) => DisasterEvent(
        id: json['id'] as String,
        name: json['name'] as String,
        type: (json['type'] as String?) ?? 'OTHER',
        startedAt: json['startedAt'] is String ? DateTime.tryParse(json['startedAt'] as String) : null,
      );

  Map<String, Object?> toRow(DateTime downloadedAt) => {
        'id': id,
        'name': name,
        'type': type,
        'started_at': startedAt?.toUtc().toIso8601String(),
        'downloaded_at': downloadedAt.toUtc().toIso8601String(),
      };

  factory DisasterEvent.fromRow(Map<String, Object?> row) => DisasterEvent(
        id: row['id'] as String,
        name: row['name'] as String,
        type: row['type'] as String,
        startedAt: row['started_at'] is String ? DateTime.parse(row['started_at'] as String) : null,
      );
}
