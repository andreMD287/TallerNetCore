using DisasterMaps.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DisasterMaps.Data.Persistence.Migrations;

/// <summary>
/// Operación desconectada: nivel de afectación 1..5, dispositivo y hora de incorporación (T_sys),
/// más la tabla intermedia y la función de la estrategia de carga en la base de datos.
/// </summary>
[DbContext(typeof(DisasterMapsDbContext))]
[Migration("20261105120000_MobileSyncIngestion")]
public sealed class MobileSyncIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE impacts ADD COLUMN affectation_level smallint;
            UPDATE impacts SET affectation_level = CASE severity
              WHEN 'LOW' THEN 1 WHEN 'MEDIUM' THEN 2 WHEN 'HIGH' THEN 3 ELSE 4 END;
            ALTER TABLE impacts ALTER COLUMN affectation_level SET NOT NULL;
            ALTER TABLE impacts ADD CONSTRAINT ck_impacts_level CHECK (affectation_level BETWEEN 1 AND 5);
            ALTER TABLE impacts ADD COLUMN device_id varchar(64);
            ALTER TABLE impacts ADD COLUMN received_at timestamptz NOT NULL DEFAULT now();
            CREATE INDEX ix_impacts_device_id ON impacts(device_id);

            -- Tabla intermedia de la estrategia DATABASE. UNLOGGED: no escribe WAL, porque su contenido
            -- es transitorio y se borra en la misma transacción que lo consolida.
            CREATE UNLOGGED TABLE impacts_staging (
              batch_id uuid NOT NULL,
              seq integer NOT NULL,
              id uuid NOT NULL,
              event_id uuid NOT NULL,
              latitude double precision NOT NULL,
              longitude double precision NOT NULL,
              severity varchar(16) NOT NULL,
              affected_people integer NOT NULL,
              municipality varchar(160) NOT NULL DEFAULT '',
              department varchar(160) NOT NULL DEFAULT '',
              description text NOT NULL DEFAULT '',
              reported_at timestamptz NOT NULL,
              source varchar(16) NOT NULL,
              status varchar(16) NOT NULL,
              affectation_level smallint NOT NULL,
              device_id varchar(64),
              PRIMARY KEY (batch_id, seq)
            );

            -- Clasifica cada registro del lote y consolida los nuevos en una sola sentencia.
            -- INSERTED: nuevo. DUPLICATE: mismo id y mismo contenido (reenvío). CONFLICT: mismo id con
            -- contenido distinto (no se sobrescribe). REJECTED: evento inexistente o datos inválidos.
            CREATE OR REPLACE FUNCTION ingest_impacts_from_staging(p_batch uuid)
            RETURNS TABLE (r_id uuid, r_outcome text, r_reason text)
            LANGUAGE plpgsql AS $$
            BEGIN
              RETURN QUERY
              WITH s AS (
                SELECT DISTINCT ON (st.id) st.*
                FROM impacts_staging st
                WHERE st.batch_id = p_batch
                ORDER BY st.id, st.seq
              ), c AS (
                SELECT s.*,
                  CASE
                    WHEN i.id IS NOT NULL
                         AND i.event_id = s.event_id AND i.latitude = s.latitude AND i.longitude = s.longitude
                         AND i.affected_people = s.affected_people AND i.affectation_level = s.affectation_level
                         AND abs(extract(epoch FROM i.reported_at - s.reported_at)) < 1 THEN 'DUPLICATE'
                    WHEN i.id IS NOT NULL THEN 'CONFLICT'
                    WHEN e.id IS NULL THEN 'NO_EVENT'
                    WHEN s.latitude NOT BETWEEN -90 AND 90 OR s.longitude NOT BETWEEN -180 AND 180
                         OR (s.latitude = 0 AND s.longitude = 0) OR s.affected_people < 0
                         OR s.affectation_level NOT BETWEEN 1 AND 5
                         OR s.severity NOT IN ('LOW','MEDIUM','HIGH','CRITICAL')
                         OR s.status NOT IN ('REPORTED','VERIFIED','ATTENDED') THEN 'INVALID'
                    ELSE 'NEW'
                  END AS cls
                FROM s
                LEFT JOIN impacts i ON i.id = s.id
                LEFT JOIN disaster_events e ON e.id = s.event_id
              ), ins AS (
                INSERT INTO impacts (id, event_id, latitude, longitude, severity, affected_people,
                  municipality, department, description, reported_at, source, status, is_deleted,
                  affectation_level, device_id)
                SELECT c.id, c.event_id, c.latitude, c.longitude, c.severity, c.affected_people,
                  c.municipality, c.department, c.description, c.reported_at, c.source, c.status, false,
                  c.affectation_level, c.device_id
                FROM c WHERE c.cls = 'NEW'
                ON CONFLICT (id) DO NOTHING
                RETURNING impacts.id
              )
              SELECT c.id,
                CASE
                  WHEN c.cls = 'NEW' AND ins.id IS NOT NULL THEN 'INSERTED'
                  WHEN c.cls = 'NEW' THEN 'DUPLICATE'
                  WHEN c.cls IN ('NO_EVENT', 'INVALID') THEN 'REJECTED'
                  ELSE c.cls
                END,
                CASE c.cls
                  WHEN 'CONFLICT' THEN 'Ya existe un registro con el mismo identificador y contenido distinto; no se sobrescribe.'
                  WHEN 'NO_EVENT' THEN 'El evento asociado no existe.'
                  WHEN 'INVALID' THEN 'Los datos incumplen las restricciones de la base de datos.'
                  ELSE ''
                END
              FROM c LEFT JOIN ins ON ins.id = c.id;

              DELETE FROM impacts_staging WHERE batch_id = p_batch;
            END
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS ingest_impacts_from_staging(uuid);
            DROP TABLE IF EXISTS impacts_staging;
            DROP INDEX IF EXISTS ix_impacts_device_id;
            ALTER TABLE impacts DROP COLUMN IF EXISTS received_at;
            ALTER TABLE impacts DROP COLUMN IF EXISTS device_id;
            ALTER TABLE impacts DROP CONSTRAINT IF EXISTS ck_impacts_level;
            ALTER TABLE impacts DROP COLUMN IF EXISTS affectation_level;
            """);
    }
}
