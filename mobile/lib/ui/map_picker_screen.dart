import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../core/coordinates.dart';
import 'widgets/osm_map.dart';

/// Elegir la ubicación tocando el mapa. Devuelve el punto elegido.
class MapPickerScreen extends StatefulWidget {
  const MapPickerScreen({super.key, this.initial});

  final LatLng? initial;

  @override
  State<MapPickerScreen> createState() => _MapPickerScreenState();
}

class _MapPickerScreenState extends State<MapPickerScreen> {
  LatLng? _point;

  @override
  void initState() {
    super.initState();
    _point = widget.initial;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Elegir ubicación')),
      body: Stack(
        children: [
          FlutterMap(
            options: MapOptions(
              initialCenter: widget.initial ?? colombiaCenter,
              initialZoom: widget.initial != null ? 15 : 6,
              onTap: (_, point) => setState(() => _point = point),
            ),
            children: [
              ...osmBaseLayers(),
              if (_point != null)
                MarkerLayer(markers: [
                  Marker(
                    point: _point!,
                    width: 48,
                    height: 48,
                    alignment: Alignment.topCenter,
                    child: Icon(Icons.location_on, size: 48, color: Theme.of(context).colorScheme.error),
                  ),
                ]),
            ],
          ),
          Positioned(
            left: 12,
            right: 12,
            top: 12,
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Text(
                  _point == null
                      ? 'Toque el mapa en el sitio afectado.'
                      : 'Latitud, longitud: ${Coordinates.formatPair(_point!.latitude, _point!.longitude)}',
                ),
              ),
            ),
          ),
        ],
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: FilledButton.icon(
            onPressed: _point == null ? null : () => Navigator.of(context).pop(_point),
            style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
            icon: const Icon(Icons.check),
            label: const Text('Usar este punto'),
          ),
        ),
      ),
    );
  }
}
