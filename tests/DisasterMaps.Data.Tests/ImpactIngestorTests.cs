using DisasterMaps.Contracts.Data.V1;
using DisasterMaps.Data.Ingestion;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Data.Tests;

public sealed class ImpactIngestorTests
{
    /// <summary>Estrategia de prueba: marca todo como insertado y registra los tamaños de paquete.</summary>
    private sealed class RecordingStrategy(IngestStrategy kind) : IImpactIngestionStrategy
    {
        public List<int> Chunks { get; } = [];
        public IngestStrategy Kind => kind;
        public Task<IReadOnlyList<IngestRow>> IngestChunkAsync(IReadOnlyList<Impact> chunk, CancellationToken ct)
        {
            Chunks.Add(chunk.Count);
            return Task.FromResult<IReadOnlyList<IngestRow>>(chunk.Select(i => new IngestRow(i.Id, IngestOutcome.Inserted)).ToList());
        }
    }

    private sealed class Monitor(IngestionOptions value) : IOptionsMonitor<IngestionOptions>
    {
        public IngestionOptions CurrentValue => value;
        public IngestionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<IngestionOptions, string?> listener) => null;
    }

    private readonly RecordingStrategy _dal = new(IngestStrategy.Dal);
    private readonly RecordingStrategy _database = new(IngestStrategy.Database);

    private ImpactIngestor Ingestor(IngestionOptions? options = null) =>
        new([_dal, _database], new Monitor(options ?? new IngestionOptions()), NullLogger<ImpactIngestor>.Instance);

    private static Impact Valid() => new()
    {
        Id = Guid.CreateVersion7().ToString(), EventId = Guid.CreateVersion7().ToString(),
        Latitude = 1.15, Longitude = -76.65, Severity = Severity.High, AffectationLevel = 3,
        Source = ImpactSource.Mobile, Status = ImpactStatus.Reported, ReportedAt = Timestamp.FromDateTime(DateTime.UtcNow),
    };

    private static IngestBatchRequest Request(int count, IngestStrategy strategy = IngestStrategy.Unspecified, int chunkSize = 0)
    {
        var request = new IngestBatchRequest { BatchId = Guid.NewGuid().ToString(), Strategy = strategy, ChunkSize = chunkSize };
        request.Impacts.AddRange(Enumerable.Range(0, count).Select(_ => Valid()));
        return request;
    }

    [Theory]
    [InlineData(10, IngestStrategy.Dal)]
    [InlineData(1999, IngestStrategy.Dal)]
    [InlineData(2000, IngestStrategy.Database)]
    public async Task Auto_selection_uses_dal_for_small_batches_and_database_for_large_ones(int count, IngestStrategy expected)
    {
        var reply = await Ingestor().IngestAsync(Request(count), default);

        Assert.Equal(expected, reply.Strategy);
    }

    [Fact]
    public async Task Requested_strategy_overrides_auto_selection()
    {
        var reply = await Ingestor().IngestAsync(Request(5, IngestStrategy.Database), default);

        Assert.Equal(IngestStrategy.Database, reply.Strategy);
        Assert.Single(_database.Chunks);
        Assert.Empty(_dal.Chunks);
    }

    [Fact]
    public async Task Fixed_default_is_used_when_auto_selection_is_off()
    {
        var reply = await Ingestor(new IngestionOptions { AutoSelect = false, DefaultStrategy = IngestStrategy.Dal })
            .IngestAsync(Request(5000), default);

        Assert.Equal(IngestStrategy.Dal, reply.Strategy);
    }

    [Fact]
    public async Task Batch_is_split_into_chunks_of_the_requested_size()
    {
        var reply = await Ingestor().IngestAsync(Request(1100, IngestStrategy.Dal, chunkSize: 500), default);

        Assert.Equal([500, 500, 100], _dal.Chunks);
        Assert.Equal(3, reply.Chunks);
        Assert.Equal(1100, reply.Results.Count);
    }

    [Fact]
    public async Task Invalid_rows_are_rejected_without_reaching_the_strategy()
    {
        var request = Request(2, IngestStrategy.Dal);
        request.Impacts[0].Latitude = 120;
        request.Impacts[1].AffectationLevel = 9;

        var reply = await Ingestor().IngestAsync(request, default);

        Assert.All(reply.Results, r => Assert.Equal(IngestOutcome.Rejected, r.Outcome));
        Assert.Empty(_dal.Chunks);
    }
}
