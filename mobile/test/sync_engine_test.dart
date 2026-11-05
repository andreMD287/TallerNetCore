import 'dart:convert';
import 'dart:io';

import 'package:campo_desastres/data/field_report.dart';
import 'package:campo_desastres/data/local_database.dart';
import 'package:campo_desastres/data/report_repository.dart';
import 'package:campo_desastres/sync/disaster_api.dart';
import 'package:campo_desastres/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:uuid/uuid.dart';

void main() {
  sqfliteFfiInit();

  late Database db;
  late ReportRepository repo;
  var now = DateTime.utc(2026, 11, 5, 12);

  setUp(() async {
    db = await LocalDatabase.open(factory: databaseFactoryFfi, path: inMemoryDatabasePath);
    repo = ReportRepository(db);
    now = DateTime.utc(2026, 11, 5, 12);
  });

  // Cerrar la base en memoria la destruye: cada prueba empieza vacía.
  tearDown(() => db.close());

  FieldReport report({int level = 3, DateTime? at}) => FieldReport(
        clientId: const Uuid().v7(),
        eventId: '01900000-0000-7000-8000-000000000001',
        eventName: 'Avalancha Mocoa',
        latitude: 1.152212,
        longitude: -76.646601,
        locationSource: LocationSource.gps,
        accuracyMeters: 6,
        affectationLevel: level,
        capturedAt: at ?? now.subtract(const Duration(hours: 2)),
      );

  /// Servidor simulado que responde con [statusFor] para cada registro y guarda los lotes recibidos.
  MockClient server(List<Map<String, dynamic>> received, {String Function(Map<String, dynamic> item)? statusFor}) =>
      MockClient((request) async {
        final body = jsonDecode(request.body) as Map<String, dynamic>;
        received.add(body);
        final results = (body['items'] as List<dynamic>).cast<Map<String, dynamic>>().map((item) {
          final status = statusFor?.call(item) ?? 'INSERTED';
          return {
            'clientId': item['clientId'],
            'status': status,
            'reason': status == 'REJECTED' ? 'El registro no cumple las reglas de negocio.' : null,
            'errors': status == 'REJECTED'
                ? {'affectationLevel': ['El nivel de afectación debe estar entre 1 y 5.']}
                : null,
          };
        }).toList();
        return http.Response(jsonEncode({'results': results, 'serverTime': now.toIso8601String()}), 200,
            headers: {'content-type': 'application/json'});
      });

  SyncEngine engine(http.Client client) =>
      SyncEngine(reports: repo, apiFor: () => DisasterApi('http://servidor', client: client), clock: () => now);

  test('envía los pendientes en lotes y los marca como sincronizados', () async {
    for (var i = 0; i < 5; i++) {
      await repo.save(report());
    }
    final received = <Map<String, dynamic>>[];

    final outcome = await engine(server(received)).run(deviceId: 'tel-1', batchSize: 2);

    expect(outcome.synced, 5);
    expect(received.length, 3); // 2 + 2 + 1
    expect(received.first['deviceId'], 'tel-1');
    final item = (received.first['items'] as List).first as Map<String, dynamic>;
    expect(item.keys, containsAll(['clientId', 'eventId', 'latitude', 'longitude', 'affectationLevel', 'reportedAt']));
    expect((await repo.counts()).synced, 5);
    expect((await repo.all()).every((r) => r.syncedAt != null), isTrue);
  });

  test('DUPLICATE también cuenta como sincronizado (reenvío tras perder la respuesta)', () async {
    await repo.save(report());

    final outcome = await engine(server([], statusFor: (_) => 'DUPLICATE')).run(deviceId: 'tel-1', batchSize: 10);

    expect(outcome.synced, 1);
    expect((await repo.counts()).synced, 1);
  });

  test('sin conexión no se pierde nada: queda pendiente con espera antes de reintentar', () async {
    final r = report();
    await repo.save(r);
    final offline = MockClient((_) async => throw const SocketException('sin red'));

    final outcome = await engine(offline).run(deviceId: 'tel-1', batchSize: 10);

    expect(outcome.failed, isTrue);
    final stored = (await repo.find(r.clientId))!;
    expect(stored.status, ReportStatus.pending);
    expect(stored.attempts, 1);
    expect(stored.lastError, 'Sin conexión con el servidor.');
    expect(stored.nextAttemptAt, now.add(const Duration(seconds: 30)));

    // Antes de la espera no se reintenta solo; con "Sincronizar ahora" (force) sí.
    final received = <Map<String, dynamic>>[];
    expect((await engine(server(received)).run(deviceId: 'tel-1', batchSize: 10)).sent, 0);
    expect((await engine(server(received)).run(deviceId: 'tel-1', batchSize: 10, force: true)).synced, 1);
  });

  test('servidor no disponible (503) se trata igual que sin conexión', () async {
    await repo.save(report());
    final unavailable = MockClient((_) async => http.Response('{"title":"x"}', 503));

    final outcome = await engine(unavailable).run(deviceId: 'tel-1', batchSize: 10);

    expect(outcome.error, 'El sistema central no está disponible en este momento.');
    expect((await repo.counts()).pending, 1);
  });

  test('rechazados y conflictos quedan para revisión con el motivo del servidor', () async {
    final bad = report(level: 5);
    final conflict = report(level: 4);
    await repo.save(bad);
    await repo.save(conflict);
    await repo.save(report());

    final outcome = await engine(server([], statusFor: (item) => switch (item['clientId']) {
          final id when id == bad.clientId => 'REJECTED',
          final id when id == conflict.clientId => 'CONFLICT',
          _ => 'INSERTED',
        })).run(deviceId: 'tel-1', batchSize: 10);

    expect(outcome.rejected, 1);
    expect(outcome.conflicts, 1);
    final rejected = (await repo.find(bad.clientId))!;
    expect(rejected.status, ReportStatus.rejected);
    expect(rejected.serverReason, contains('entre 1 y 5'));
    expect((await repo.find(conflict.clientId))!.status, ReportStatus.conflict);

    // No se reintentan solos.
    final received = <Map<String, dynamic>>[];
    await engine(server(received)).run(deviceId: 'tel-1', batchSize: 10, force: true);
    expect(received, isEmpty);
  });

  test('la espera entre reintentos crece y tiene tope', () {
    expect(SyncEngine.backoff(1), const Duration(seconds: 30));
    expect(SyncEngine.backoff(2), const Duration(minutes: 1));
    expect(SyncEngine.backoff(3), const Duration(minutes: 2));
    expect(SyncEngine.backoff(20), const Duration(minutes: 15));
  });

  test('los reportes ya enviados no se pueden borrar desde el teléfono', () async {
    final r = report();
    await repo.save(r);
    await engine(server([])).run(deviceId: 'tel-1', batchSize: 10);

    expect(await repo.delete(r.clientId), isFalse);
    expect(await repo.deleteSynced(), 1);
  });
}
