namespace DisasterMaps.Business.Domain;

// Los valores numéricos coinciden con los enums del .proto (0 = UNSPECIFIED no existe en el dominio).
// En JSON se serializan en MAYÚSCULAS (LOW, LANDSLIDE, ...) con JsonStringEnumConverter(SnakeCaseUpper).

public enum EventType { Flood = 1, Landslide = 2, Earthquake = 3, Wildfire = 4, Windstorm = 5, Other = 6 }

public enum Severity { Low = 1, Medium = 2, High = 3, Critical = 4 }

public enum ImpactSource { Manual = 1, File = 2, Device = 3 }

public enum ImpactStatus { Reported = 1, Verified = 2, Attended = 3 }
