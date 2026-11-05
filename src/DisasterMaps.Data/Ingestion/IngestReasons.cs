namespace DisasterMaps.Data.Ingestion;

/// <summary>Motivos en español; los mismos textos que devuelve la función de la base de datos.</summary>
public static class IngestReasons
{
    public const string Conflict = "Ya existe un registro con el mismo identificador y contenido distinto; no se sobrescribe.";
    public const string MissingEvent = "El evento asociado no existe.";
}
