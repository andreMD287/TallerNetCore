import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../core/affectation_level.dart';
import '../core/coordinates.dart';
import '../data/field_report.dart';
import '../state/app_state.dart';
import 'report_detail_screen.dart';
import 'widgets/level_selector.dart';
import 'widgets/osm_map.dart';

/// Reportes del teléfono sobre el mapa, coloreados por nivel de afectación.
class MapScreen extends StatelessWidget {
  const MapScreen({super.key, required this.state, required this.active});

  final AppState state;

  /// Solo se consulta la base cuando la pestaña está visible.
  final bool active;

  @override
  Widget build(BuildContext context) {
    if (!active) return const SizedBox.shrink();
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) => FutureBuilder<List<FieldReport>>(
        key: ValueKey(state.revision),
        future: state.reports.all(),
        builder: (context, snapshot) {
          final items = snapshot.data ?? const <FieldReport>[];
          final points = items.map((r) => LatLng(r.latitude, r.longitude)).toList();
          return Stack(
            children: [
              FlutterMap(
                options: MapOptions(
                  initialCenter: points.isEmpty ? colombiaCenter : points.first,
                  initialZoom: points.isEmpty ? 6 : 12,
                  initialCameraFit: points.length > 1
                      ? CameraFit.coordinates(coordinates: points, padding: const EdgeInsets.all(48), maxZoom: 16)
                      : null,
                ),
                children: [
                  ...osmBaseLayers(),
                  MarkerLayer(markers: [
                    for (final r in items)
                      Marker(
                        point: LatLng(r.latitude, r.longitude),
                        width: 34,
                        height: 34,
                        child: GestureDetector(
                          onTap: () => _showReport(context, r),
                          child: Opacity(
                            opacity: r.status == ReportStatus.synced ? 1 : 0.8,
                            child: LevelBadge(level: r.affectationLevel, size: 34),
                          ),
                        ),
                      ),
                  ]),
                ],
              ),
              Positioned(left: 8, bottom: 24, child: _legend(context)),
            ],
          );
        },
      ),
    );
  }

  Widget _legend(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final level in AffectationLevel.values.reversed)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 2),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      LevelBadge(level: level, size: 18),
                      const SizedBox(width: 6),
                      Text(AffectationLevel.label(level), style: const TextStyle(fontSize: 12)),
                    ],
                  ),
                ),
            ],
          ),
        ),
      );

  void _showReport(BuildContext context, FieldReport r) {
    showModalBottomSheet<void>(
      context: context,
      builder: (sheet) => SafeArea(
        child: ListTile(
          leading: LevelBadge(level: r.affectationLevel),
          title: Text('Nivel ${r.affectationLevel} · ${AffectationLevel.label(r.affectationLevel)}'),
          subtitle: Text('${Coordinates.formatPair(r.latitude, r.longitude)}\n${r.status.label} · ${r.eventName}'),
          isThreeLine: true,
          trailing: const Icon(Icons.chevron_right),
          onTap: () {
            Navigator.pop(sheet);
            Navigator.of(context).push(MaterialPageRoute(
              builder: (_) => ReportDetailScreen(state: state, clientId: r.clientId),
            ));
          },
        ),
      ),
    );
  }
}
