// Prueba de contrato contra el sistema central real (no simulado).
// Se omite salvo que se indique la dirección del servidor:
//   docker compose up -d --build
//   flutter test test/live_server_test.dart --dart-define=SERVER_URL=http://localhost:5002
@Tags(['servidor'])
library;

import 'package:campo_desastres/data/field_report.dart';
import 'package:campo_desastres/data/local_database.dart';
import 'package:campo_desastres/data/report_repository.dart';
import 'package:campo_desastres/sync/disaster_api.dart';
import 'package:campo_desastres/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:uuid/uuid.dart';

const serverUrl = String.fromEnvironment('SERVER_URL');

void main() {
  sqfliteFfiInit();

  test('captura sin conexión, sincroniza con el sistema central real y reenviar no duplica', () async {
    final api = DisasterApi(serverUrl);
    expect(await api.isAvailable(), isTrue, reason: 'El sistema central debe estar arriba en $serverUrl');
    final events = await api.fetchEvents();
    expect(events, isNotEmpty);

    final db = await LocalDatabase.open(factory: databaseFactoryFfi, path: inMemoryDatabasePath);
    final repo = ReportRepository(db);
    final good = <FieldReport>[
      for (var level = 1; level <= 5; level++)
        FieldReport(
          clientId: const Uuid().v7(),
          eventId: events.first.id,
          eventName: events.first.name,
          latitude: 1.15 + level / 1000,
          longitude: -76.65 - level / 1000,
          locationSource: LocationSource.gps,
          affectationLevel: level,
          affectedPeople: level * 10,
          municipality: 'Mocoa',
          department: 'Putumayo',
          capturedAt: DateTime.now().toUtc().subtract(Duration(hours: level)),
        ),
    ];
    // Evento que no existe en el sistema central: debe volver rechazado sin bloquear a los demás.
    final orphan = good.first.copyWith(clientId: const Uuid().v7(), eventId: const Uuid().v7());
    for (final r in [...good, orphan]) {
      await repo.save(r);
    }

    final engine = SyncEngine(reports: repo, apiFor: () => DisasterApi(serverUrl));
    final first = await engine.run(deviceId: 'prueba-contrato', batchSize: 4);

    expect(first.failed, isFalse, reason: first.error);
    expect(first.synced, 5);
    expect(first.rejected, 1);
    expect((await repo.find(orphan.clientId))!.serverReason, contains('evento'));

    // El teléfono no recibió la respuesta y reenvía: el servidor responde DUPLICATE.
    for (final r in good) {
      await repo.save(r);
    }
    final again = await engine.run(deviceId: 'prueba-contrato', batchSize: 10);
    expect(again.synced, 5);
    expect((await repo.counts()).synced, 5);

    await db.close();
    api.close();
  }, skip: serverUrl.isEmpty ? 'Defina SERVER_URL para probar contra el sistema central real.' : false);
}
