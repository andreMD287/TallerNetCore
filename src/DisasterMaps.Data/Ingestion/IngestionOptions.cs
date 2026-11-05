using DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Data.Ingestion;

/// <summary>Sección "Ingestion" de appsettings.</summary>
public sealed class IngestionOptions
{
    public const string Section = "Ingestion";
    public const int MaxChunkSize = 5000;

    /// <summary>
    /// true: si la solicitud no indica estrategia, se elige por tamaño del lote (DAL para lotes pequeños,
    /// DATABASE desde <see cref="AutoThreshold"/>), según la medición de scripts/benchmark-ingesta.ps1.
    /// </summary>
    public bool AutoSelect { get; set; } = true;
    /// <summary>Registros a partir de los cuales conviene la estrategia DATABASE.</summary>
    public int AutoThreshold { get; set; } = 2000;
    /// <summary>Estrategia fija cuando <see cref="AutoSelect"/> es false: DAL o DATABASE.</summary>
    public IngestStrategy DefaultStrategy { get; set; } = IngestStrategy.Database;
    /// <summary>Registros por transacción.</summary>
    public int ChunkSize { get; set; } = 500;
}
