using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Business.Visualization;

public sealed class VisualizationRequest
{
    public Guid EventId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string Method { get; set; } = "IDW";
    public string Metric { get; set; } = "severity";
    public double RadiusKm { get; set; } = 15;
    public double CellKm { get; set; } = 1;
    public int MaxCells { get; set; } = 2500;
}

public sealed record InterpolationResponse(
    Guid EventId,
    string Method,
    string Metric,
    int SampleCount,
    SpatialGrid Grid,
    IReadOnlyList<AffectationLevel> Levels);

public sealed record ComparisonResponse(
    Guid EventId,
    string Metric,
    int SampleCount,
    IReadOnlyList<ValidationMetrics> Methods);

public sealed class VisualizationService(
    IDataGateway gateway,
    IOptionsMonitor<SeverityThresholds> thresholds)
{
    private const int MaxSamples = 500;

    public async Task<InterpolationResponse> InterpolateAsync(
        VisualizationRequest request, CancellationToken ct)
    {
        Validate(request);

        var samples = await LoadSamplesAsync(request, ct);

        ISpatialInterpolator model = CreateModel(request.Method);

        if (model is OrdinaryKrigingInterpolator && samples.Count < 5)
            throw new ArgumentException(
                "Kriging requiere al menos cinco ubicaciones.");

        var parameters = new InterpolationParameters(
            Power: 2,
            Neighbors: 12,
            MaxDistanceKm: request.RadiusKm);

        model.Fit(samples, parameters);

        var grid = GridBuilder.Build(
            samples,
            model,
            maxDistanceKm: request.RadiusKm,
            cellSizeKm: request.CellKm,
            maxCells: request.MaxCells);

        return new InterpolationResponse(
            request.EventId,
            model.Method,
            request.Metric.ToLowerInvariant(),
            samples.Count,
            grid,
            GetLevels(request.Metric));
    }

    public async Task<ComparisonResponse> CompareAsync(
        VisualizationRequest request, CancellationToken ct)
    {
        Validate(request);

        var samples = await LoadSamplesAsync(request, ct);

        if (samples.Count > 100)
            throw new ArgumentException(
                "La comparacion permite hasta 100 muestras por consulta.");

        var parameters = new InterpolationParameters(
            Power: 2,
            Neighbors: 12,
            MaxDistanceKm: request.RadiusKm);

        var methods = new ValidationMetrics[]
        {
            CrossValidator.Evaluate(
                samples, () => new IdwInterpolator(), parameters),
            CrossValidator.Evaluate(
                samples, () => new NearestNeighborInterpolator(), parameters),
            CrossValidator.Evaluate(
                samples, () => new OrdinaryKrigingInterpolator(), parameters)
        };

        return new ComparisonResponse(
            request.EventId,
            request.Metric.ToLowerInvariant(),
            samples.Count,
            methods);
    }

    private async Task<List<SamplePoint>> LoadSamplesAsync(
        VisualizationRequest request, CancellationToken ct)
    {
        var samples = new List<SamplePoint>();

        for (int page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await gateway.ListImpactsAsync(
                new ImpactQuery
                {
                    EventId = request.EventId,
                    From = request.From,
                    To = request.To,
                    Page = page,
                    PageSize = 200
                }, ct);

            if (result.Total > MaxSamples)
                throw new ArgumentException(
                    $"El evento supera el limite de {MaxSamples} muestras.");

            foreach (var item in result.Items)
            {
                double value = request.Metric.Equals(
                    "people", StringComparison.OrdinalIgnoreCase)
                    ? AffectationLevelClassifier.ToPeopleScale(
                        item.AffectedPeople)
                    : (int)item.Severity;

                if (!double.IsFinite(item.Latitude) ||
                    !double.IsFinite(item.Longitude) ||
                    !double.IsFinite(value))
                    continue;

                samples.Add(new SamplePoint(
                    item.Latitude, item.Longitude, value));
            }

            if (samples.Count > MaxSamples)
                throw new ArgumentException(
                    "Demasiadas muestras para interpolacion.");

            if (result.Items.Count == 0 ||
                (long)page * 200 >= result.Total)
                break;
        }

        if (samples.Count == 0)
            throw new ArgumentException(
                "No hay afectaciones para los filtros seleccionados.");

        return samples;
    }

    private IReadOnlyList<AffectationLevel> GetLevels(string metric)
    {
        if (metric.Equals("severity", StringComparison.OrdinalIgnoreCase))
            return AffectationLevelClassifier.SeverityLevels;

        var t = thresholds.CurrentValue;

        return AffectationLevelClassifier.PeopleLevels(
            t.Medium,
            t.High,
            t.Critical);
    }

    private static ISpatialInterpolator CreateModel(string method) =>
        method.ToUpperInvariant() switch
        {
            "IDW" => new IdwInterpolator(),
            "NEAREST" => new NearestNeighborInterpolator(),
            "KRIGING" => new OrdinaryKrigingInterpolator(),
            _ => throw new ArgumentException(
                "Metodo invalido. Use IDW, NEAREST o KRIGING.")
        };

    private static void Validate(VisualizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EventId == Guid.Empty)
            throw new ArgumentException("eventId es obligatorio.");

        if (request.From > request.To)
            throw new ArgumentException("Rango de fechas invalido.");

        if (string.IsNullOrWhiteSpace(request.Method) ||
            !new[] { "IDW", "NEAREST", "KRIGING" }
                .Contains(request.Method, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Metodo invalido.");

        if (string.IsNullOrWhiteSpace(request.Metric) ||
            !new[] { "severity", "people" }
                .Contains(request.Metric, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Metrica invalida.");

        if (!double.IsFinite(request.RadiusKm) ||
            request.RadiusKm is < 0.1 or > 50)
            throw new ArgumentException("radiusKm debe estar entre 0.1 y 50.");

        if (!double.IsFinite(request.CellKm) ||
            request.CellKm is < 0.1 or > 20)
            throw new ArgumentException("cellKm debe estar entre 0.1 y 20.");

        if (request.MaxCells is < 1 or > 40000)
            throw new ArgumentException("maxCells debe estar entre 1 y 40000.");
    }
}