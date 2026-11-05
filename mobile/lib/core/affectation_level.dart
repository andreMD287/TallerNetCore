import 'package:flutter/material.dart';

/// Nivel de afectación de un sitio de desastre, de 1 (mínimo) a 5 (máximo).
/// Los colores coinciden con la escala del mapa de calor del sistema central.
class AffectationLevel {
  AffectationLevel._();

  static const int min = 1;
  static const int max = 5;
  static const List<int> values = [1, 2, 3, 4, 5];

  static const Map<int, String> _labels = {
    1: 'Mínima',
    2: 'Leve',
    3: 'Moderada',
    4: 'Grave',
    5: 'Crítica',
  };

  static const Map<int, String> _descriptions = {
    1: 'Daños menores, sin riesgo para las personas.',
    2: 'Daños leves en viviendas, vías o servicios.',
    3: 'Daños moderados; el sitio requiere atención.',
    4: 'Daños graves; hay personas en riesgo.',
    5: 'Destrucción total o personas atrapadas.',
  };

  static const Map<int, Color> _colors = {
    1: Color(0xFF38A169),
    2: Color(0xFF9BC53D),
    3: Color(0xFFE5B135),
    4: Color(0xFFF07835),
    5: Color(0xFFD53745),
  };

  static bool isValid(int? level) => level != null && level >= min && level <= max;

  static String label(int level) => _labels[level] ?? '—';

  static String description(int level) => _descriptions[level] ?? '';

  static Color color(int level) => _colors[level] ?? Colors.grey;

  /// Color de texto legible sobre el color del nivel.
  static Color onColor(int level) => level == 2 || level == 3 ? Colors.black87 : Colors.white;
}
