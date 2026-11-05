namespace DisasterMaps.Business.DataGateway;

/// <summary>Sección "DataTier" de appsettings (equivalente al lookup JNDI de la dirección del servicio remoto).</summary>
public sealed class DataTierOptions
{
    public const string Section = "DataTier";

    /// <summary>true = FakeDataGateway en memoria; false = cliente gRPC real.</summary>
    public bool UseFake { get; set; }
    public string Address { get; set; } = "http://localhost:5003";
    /// <summary>Deadline por llamada gRPC (incluye los reintentos).</summary>
    public double DeadlineSeconds { get; set; } = 3;
    /// <summary>Deadline de la carga por lotes (lotes de hasta 10 000 registros).</summary>
    public double BatchDeadlineSeconds { get; set; } = 60;
}
