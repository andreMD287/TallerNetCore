using DisasterMaps.Contracts.Data.V1;
using Npgsql;
using NpgsqlTypes;

namespace DisasterMaps.Data.Repositories;

/// <summary>Inserción masiva con COPY binario de Npgsql (ADO.NET).</summary>
public sealed class ImpactBulkWriter(NpgsqlDataSource dataSource)
{
    private const string Columns = "id, event_id, latitude, longitude, severity, affected_people, municipality, " +
                                   "department, description, reported_at, source, status, affectation_level, device_id";

    /// <summary>Lote atómico (importación de archivos): todo o nada.</summary>
    public async Task<int> WriteAsync(IEnumerable<Impact> rows, CancellationToken ct)
    {
        var items = rows.ToList();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await CopyAsync(connection, items, stagingBatch: null, ct);
        await tx.CommitAsync(ct);
        return items.Count;
    }

    /// <summary>
    /// COPY de afectaciones dentro de la transacción abierta en <paramref name="connection"/>:
    /// a <c>impacts</c>, o a <c>impacts_staging</c> si se indica el lote intermedio.
    /// </summary>
    public static async Task CopyAsync(NpgsqlConnection connection, IReadOnlyList<Impact> items, Guid? stagingBatch, CancellationToken ct)
    {
        var target = stagingBatch is null ? $"impacts ({Columns})" : $"impacts_staging (batch_id, seq, {Columns})";
        await using var writer = await connection.BeginBinaryImportAsync($"COPY {target} FROM STDIN (FORMAT BINARY)", ct);
        var seq = 0;
        foreach (var i in items)
        {
            await writer.StartRowAsync(ct);
            if (stagingBatch is { } batch)
            {
                await writer.WriteAsync(batch, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(seq++, NpgsqlDbType.Integer, ct);
            }
            await writer.WriteAsync(Guid.Parse(i.Id), NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(Guid.Parse(i.EventId), NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(i.Latitude, NpgsqlDbType.Double, ct);
            await writer.WriteAsync(i.Longitude, NpgsqlDbType.Double, ct);
            await writer.WriteAsync(i.Severity.ToString().ToUpperInvariant(), NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(i.AffectedPeople, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(i.Municipality ?? "", NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(i.Department ?? "", NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(i.Description ?? "", NpgsqlDbType.Text, ct);
            await writer.WriteAsync(i.ReportedAt.ToDateTime(), NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(i.Source.ToString().ToUpperInvariant(), NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(i.Status.ToString().ToUpperInvariant(), NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(LevelOf(i), NpgsqlDbType.Smallint, ct);
            if (string.IsNullOrEmpty(i.DeviceId)) await writer.WriteNullAsync(ct);
            else await writer.WriteAsync(i.DeviceId, NpgsqlDbType.Varchar, ct);
        }
        await writer.CompleteAsync(ct);
    }

    /// <summary>Nivel 1..5; si no viene (0) se deriva de la severidad: LOW=1, MEDIUM=2, HIGH=3, CRITICAL=4.</summary>
    public static short LevelOf(Impact i) =>
        i.AffectationLevel is >= 1 and <= 5 ? (short)i.AffectationLevel : (short)Math.Clamp((int)i.Severity, 1, 4);
}
