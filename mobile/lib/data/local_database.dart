import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

/// Base de datos SQLite del teléfono: los reportes y los eventos descargados.
/// Es la fuente de verdad mientras no hay conexión; nada se borra hasta que el sistema central lo confirma.
class LocalDatabase {
  LocalDatabase._();

  static const String fileName = 'campo_desastres.db';
  static const int version = 1;

  static Future<Database> open({DatabaseFactory? factory, String? path}) async {
    final dbFactory = factory ?? databaseFactory;
    final dbPath = path ?? p.join(await dbFactory.getDatabasesPath(), fileName);
    return dbFactory.openDatabase(
      dbPath,
      options: OpenDatabaseOptions(version: version, onCreate: _create),
    );
  }

  static Future<void> _create(Database db, int version) async {
    await db.execute('''
      CREATE TABLE reports (
        client_id TEXT PRIMARY KEY,
        event_id TEXT NOT NULL,
        event_name TEXT NOT NULL,
        latitude REAL NOT NULL CHECK (latitude BETWEEN -90 AND 90),
        longitude REAL NOT NULL CHECK (longitude BETWEEN -180 AND 180),
        accuracy_m REAL,
        location_source TEXT NOT NULL,
        affectation_level INTEGER NOT NULL CHECK (affectation_level BETWEEN 1 AND 5),
        affected_people INTEGER NOT NULL DEFAULT 0 CHECK (affected_people >= 0),
        municipality TEXT NOT NULL DEFAULT '',
        department TEXT NOT NULL DEFAULT '',
        description TEXT NOT NULL DEFAULT '',
        captured_at TEXT NOT NULL,
        status TEXT NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0,
        last_error TEXT,
        server_reason TEXT,
        synced_at TEXT,
        next_attempt_at TEXT
      )''');
    await db.execute('CREATE INDEX ix_reports_status ON reports(status, captured_at)');
    await db.execute('''
      CREATE TABLE events (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        type TEXT NOT NULL,
        started_at TEXT,
        downloaded_at TEXT NOT NULL
      )''');
  }
}
