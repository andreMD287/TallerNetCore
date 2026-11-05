/// Validación de coordenadas WGS84 con las mismas reglas del sistema central (RN-01).
class Coordinates {
  Coordinates._();

  static const int decimals = 6;

  /// Acepta punto o coma decimal.
  static double? parse(String? text) {
    if (text == null) return null;
    final clean = text.trim().replaceAll(',', '.');
    if (clean.isEmpty) return null;
    final value = double.tryParse(clean);
    return value != null && value.isFinite ? value : null;
  }

  static String? validateLatitude(String? text) {
    if (text == null || text.trim().isEmpty) return 'La latitud es obligatoria.';
    final value = parse(text);
    if (value == null) return 'Escriba un número, por ejemplo 1.152212.';
    if (value < -90 || value > 90) return 'La latitud debe estar entre -90 y 90.';
    return null;
  }

  static String? validateLongitude(String? text) {
    if (text == null || text.trim().isEmpty) return 'La longitud es obligatoria.';
    final value = parse(text);
    if (value == null) return 'Escriba un número, por ejemplo -76.646601.';
    if (value < -180 || value > 180) return 'La longitud debe estar entre -180 y 180.';
    return null;
  }

  /// (0, 0) suele indicar una coordenada vacía y el sistema central la rechaza.
  static String? validatePair(double latitude, double longitude) =>
      latitude == 0 && longitude == 0 ? 'La coordenada (0, 0) no es válida.' : null;

  static String format(double value) => value.toStringAsFixed(decimals);

  static String formatPair(double latitude, double longitude) =>
      '${format(latitude)}, ${format(longitude)}';
}
