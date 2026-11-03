using DisasterMaps.Contracts.Data.V1;
using Npgsql;
using NpgsqlTypes;

namespace DisasterMaps.Data.Repositories;

public sealed class ImpactBulkWriter(NpgsqlDataSource dataSource)
{
    public async Task<int> WriteAsync(IEnumerable<Impact> rows, CancellationToken ct)
    {
        var items = rows.ToList();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var writer = await connection.BeginBinaryImportAsync("""
            COPY impacts (id, event_id, latitude, longitude, severity, affected_people,
              municipality, department, description, reported_at, source, status, is_deleted)
            FROM STDIN (FORMAT BINARY)
            """, ct))
        {
            foreach (var i in items)
            {
                await writer.StartRowAsync(ct);
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
                await writer.WriteAsync(false, NpgsqlDbType.Boolean, ct);
            }
            await writer.CompleteAsync(ct);
        }
        await tx.CommitAsync(ct);
        return items.Count;
    }
}
