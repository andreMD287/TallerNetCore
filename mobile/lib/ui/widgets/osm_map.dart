import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

/// Centro por defecto: Colombia.
const LatLng colombiaCenter = LatLng(4.6, -74.1);

/// Capa base de OpenStreetMap con su atribución. Sin conexión, las teselas que no se hayan visto
/// antes no cargan, pero los marcadores y la selección de puntos siguen funcionando.
List<Widget> osmBaseLayers() => [
      TileLayer(
        urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        userAgentPackageName: 'co.edu.javeriana.disastermaps.campo_desastres',
        maxNativeZoom: 19,
      ),
      const RichAttributionWidget(
        attributions: [TextSourceAttribution('© colaboradores de OpenStreetMap')],
      ),
    ];
