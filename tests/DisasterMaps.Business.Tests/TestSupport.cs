using DisasterMaps.Business.Domain;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Business.Tests;

/// <summary>Reloj fijo para probar RN-05 sin depender de la hora real.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>IOptionsMonitor mínimo cuyo valor se puede cambiar en caliente (simula recargar appsettings).</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class Samples
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    public static string Path(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "samples", file);

    public static SeverityPolicy DefaultSeverityPolicy() => new(new TestOptionsMonitor<SeverityThresholds>(new SeverityThresholds()));
}
