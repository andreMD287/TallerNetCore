import 'package:sqflite/sqflite.dart';

import 'disaster_event.dart';
import 'field_report.dart';

/// Conteo de reportes por estado.
class ReportCounts {
  const ReportCounts({this.pending = 0, this.synced = 0, this.conflict = 0, this.rejected = 0});

  final int pending;
  final int synced;
  final int conflict;
  final int rejected;

  int get needsAttention => conflict + rejected;
  int get total => pending + synced + conflict + rejected;
}

/// Acceso a los reportes guardados en el teléfono.
class ReportRepository {
  ReportRepository(this._db);

  final Database _db;

  Future<void> save(FieldReport report) =>
      _db.insert('reports', report.toRow(), conflictAlgorithm: ConflictAlgorithm.replace);

  Future<FieldReport?> find(String clientId) async {
    final rows = await _db.query('reports', where: 'client_id = ?', whereArgs: [clientId]);
    return rows.isEmpty ? null : FieldReport.fromRow(rows.first);
  }

  /// Todos los reportes, del más reciente al más antiguo; opcionalmente solo los de ciertos estados.
  Future<List<FieldReport>> all({Set<ReportStatus>? statuses}) async {
    final rows = statuses == null
        ? await _db.query('reports', orderBy: 'captured_at DESC')
        : await _db.query(
            'reports',
            where: 'status IN (${List.filled(statuses.length, '?').join(',')})',
            whereArgs: statuses.map((s) => s.code).toList(),
            orderBy: 'captured_at DESC',
          );
    return rows.map(FieldReport.fromRow).toList();
  }

  /// Pendientes listos para enviar, en orden de captura. Con [now] nulo ignora la espera entre reintentos.
  Future<List<FieldReport>> pendingDue({DateTime? now, required int limit}) async {
    final rows = now == null
        ? await _db.query('reports',
            where: 'status = ?', whereArgs: [ReportStatus.pending.code], orderBy: 'captured_at', limit: limit)
        : await _db.query('reports',
            where: 'status = ? AND (next_attempt_at IS NULL OR next_attempt_at <= ?)',
            whereArgs: [ReportStatus.pending.code, now.toUtc().toIso8601String()],
            orderBy: 'captured_at',
            limit: limit);
    return rows.map(FieldReport.fromRow).toList();
  }

  Future<ReportCounts> counts() async {
    final rows = await _db.rawQuery('SELECT status, COUNT(*) AS n FROM reports GROUP BY status');
    final byStatus = {for (final r in rows) ReportStatus.fromCode(r['status'] as String): r['n'] as int};
    return ReportCounts(
      pending: byStatus[ReportStatus.pending] ?? 0,
      synced: byStatus[ReportStatus.synced] ?? 0,
      conflict: byStatus[ReportStatus.conflict] ?? 0,
      rejected: byStatus[ReportStatus.rejected] ?? 0,
    );
  }

  /// Aplica la respuesta del sistema central en una sola transacción.
  Future<void> applyResults(Map<String, ({ReportStatus status, String? reason})> results, DateTime now) =>
      _db.transaction((txn) async {
        for (final entry in results.entries) {
          final synced = entry.value.status == ReportStatus.synced;
          await txn.update(
            'reports',
            {
              'status': entry.value.status.code,
              'server_reason': entry.value.reason,
              'last_error': null,
              'next_attempt_at': null,
              'synced_at': synced ? now.toUtc().toIso8601String() : null,
            },
            where: 'client_id = ?',
            whereArgs: [entry.key],
          );
        }
      });

  /// El envío falló por conexión o por el servidor: los reportes siguen pendientes y esperan antes de reintentar.
  Future<void> markFailure(List<FieldReport> reports, String error, DateTime Function(int attempts) nextAttempt) =>
      _db.transaction((txn) async {
        for (final report in reports) {
          final attempts = report.attempts + 1;
          await txn.update(
            'reports',
            {
              'attempts': attempts,
              'last_error': error,
              'next_attempt_at': nextAttempt(attempts).toUtc().toIso8601String(),
            },
            where: 'client_id = ?',
            whereArgs: [report.clientId],
          );
        }
      });

  /// Solo se pueden borrar reportes que no están en el sistema central.
  Future<bool> delete(String clientId) async {
    final count = await _db.delete('reports',
        where: 'client_id = ? AND status <> ?', whereArgs: [clientId, ReportStatus.synced.code]);
    return count > 0;
  }

  /// Limpia del teléfono los reportes que el sistema central ya confirmó.
  Future<int> deleteSynced() =>
      _db.delete('reports', where: 'status = ?', whereArgs: [ReportStatus.synced.code]);
}

/// Eventos descargados del sistema central.
class EventRepository {
  EventRepository(this._db);

  final Database _db;

  Future<void> replaceAll(List<DisasterEvent> events, DateTime downloadedAt) => _db.transaction((txn) async {
        await txn.delete('events');
        for (final e in events) {
          await txn.insert('events', e.toRow(downloadedAt));
        }
      });

  Future<List<DisasterEvent>> all() async =>
      (await _db.query('events', orderBy: 'started_at DESC')).map(DisasterEvent.fromRow).toList();

  Future<DateTime?> lastDownload() async {
    final rows = await _db.rawQuery('SELECT MAX(downloaded_at) AS d FROM events');
    final value = rows.first['d'];
    return value is String ? DateTime.parse(value) : null;
  }
}
