namespace DisasterMaps.Business.Application;

/// <summary>Sección "Sync" de appsettings.</summary>
public sealed class SyncOptions
{
    public const string Section = "Sync";
    public const int MaxDeviceIdLength = 64;
    public const int MaxChunkSize = 5000;

    /// <summary>Registros por solicitud (igual que el tope de filas de la importación, RN-06).</summary>
    public int MaxItems { get; set; } = 10_000;
}
