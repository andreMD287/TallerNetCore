using DisasterMaps.Data.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DisasterMaps.Data.Persistence.Migrations;

[DbContext(typeof(DisasterMapsDbContext))]
[Migration("20261103120000_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE disaster_events (
              id uuid PRIMARY KEY,
              name varchar(200) NOT NULL,
              type varchar(40) NOT NULL,
              started_at timestamptz NOT NULL,
              description text NOT NULL DEFAULT ''
            );
            CREATE TABLE impacts (
              id uuid PRIMARY KEY,
              event_id uuid NOT NULL REFERENCES disaster_events(id) ON DELETE RESTRICT,
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
              is_deleted boolean NOT NULL DEFAULT false,
              CONSTRAINT ck_impacts_latitude CHECK (latitude BETWEEN -90 AND 90),
              CONSTRAINT ck_impacts_longitude CHECK (longitude BETWEEN -180 AND 180),
              CONSTRAINT ck_impacts_not_zero CHECK (NOT (latitude = 0 AND longitude = 0)),
              CONSTRAINT ck_impacts_people CHECK (affected_people >= 0),
              CONSTRAINT ck_impacts_severity CHECK (severity IN ('LOW','MEDIUM','HIGH','CRITICAL')),
              CONSTRAINT ck_impacts_status CHECK (status IN ('REPORTED','VERIFIED','ATTENDED'))
            );
            CREATE INDEX ix_impacts_event_id ON impacts(event_id);
            CREATE INDEX ix_impacts_coordinates ON impacts(latitude, longitude);
            CREATE INDEX ix_impacts_reported_at ON impacts(reported_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS impacts; DROP TABLE IF EXISTS disaster_events;");
    }
}
