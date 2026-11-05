import 'package:campo_desastres/core/affectation_level.dart';
import 'package:campo_desastres/core/coordinates.dart';
import 'package:campo_desastres/data/field_report.dart';
import 'package:campo_desastres/data/settings_store.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Coordenadas (RN-01)', () {
    test('acepta punto o coma decimal', () {
      expect(Coordinates.parse('1.152212'), 1.152212);
      expect(Coordinates.parse(' -76,646601 '), -76.646601);
      expect(Coordinates.parse('abc'), isNull);
    });

    test('valida rangos', () {
      expect(Coordinates.validateLatitude('90'), isNull);
      expect(Coordinates.validateLatitude('90.1'), isNotNull);
      expect(Coordinates.validateLongitude('-180'), isNull);
      expect(Coordinates.validateLongitude('181'), isNotNull);
      expect(Coordinates.validateLatitude(''), 'La latitud es obligatoria.');
    });

    test('rechaza (0, 0) y formatea con 6 decimales', () {
      expect(Coordinates.validatePair(0, 0), isNotNull);
      expect(Coordinates.validatePair(0, -76.6), isNull);
      expect(Coordinates.formatPair(1.15, -76.6), '1.150000, -76.600000');
    });
  });

  test('niveles de afectación 1 a 5', () {
    expect(AffectationLevel.values, [1, 2, 3, 4, 5]);
    expect(AffectationLevel.isValid(0), isFalse);
    expect(AffectationLevel.isValid(6), isFalse);
    expect(AffectationLevel.label(5), 'Crítica');
  });

  test('el reporte sobrevive a la base local y genera el JSON del contrato', () {
    final r = FieldReport(
      clientId: 'id-1',
      eventId: 'ev-1',
      eventName: 'Evento',
      latitude: 1.152212,
      longitude: -76.646601,
      locationSource: LocationSource.map,
      affectationLevel: 4,
      affectedPeople: 12,
      capturedAt: DateTime.utc(2026, 11, 5, 9, 12, 44),
    );

    final back = FieldReport.fromRow(r.toRow());
    expect(back.latitude, r.latitude);
    expect(back.longitude, r.longitude);
    expect(back.affectationLevel, 4);
    expect(back.locationSource, LocationSource.map);
    expect(back.capturedAt, r.capturedAt);

    final json = r.toSyncJson();
    expect(json['reportedAt'], '2026-11-05T09:12:44.000Z');
    expect(json['municipality'], isNull); // vacío se envía como null
    expect(json['affectationLevel'], 4);
  });

  test('normaliza la dirección del servidor', () {
    expect(SettingsStore.normalizeUrl(' 192.168.1.20:5002/ '), 'http://192.168.1.20:5002');
    expect(SettingsStore.normalizeUrl('https://ejemplo.org'), 'https://ejemplo.org');
  });
}
