// Simulador de dispositivo de campo: demuestra el requisito "recibir coordenadas" en vivo.
// Envía N afectaciones aleatorias dentro de un bbox a POST /api/v1/impacts con source = DEVICE.
//
// Uso:
//   dotnet run --project src/DisasterMaps.Simulator -- --event-id <uuid> [--count 20] [--interval 2]
//       [--bbox minLat,minLon,maxLat,maxLon] [--business-url http://localhost:5002]
using System.Globalization;
using System.Net.Http.Json;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = SimulatorOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Uso: DisasterMaps.Simulator --event-id <uuid> [--count 20] [--interval 2]
                                    [--bbox minLat,minLon,maxLat,maxLon] [--business-url http://localhost:5002]
        Por defecto el bbox cubre Mocoa (Putumayo): 1.10,-76.70,1.20,-76.60
        """);
    return 2;
}

using var http = new HttpClient { BaseAddress = new Uri(options.BusinessUrl), Timeout = TimeSpan.FromSeconds(10) };
var random = new Random();
string[] municipalities = ["Zona A", "Zona B", "Zona C", "Zona D"];
var ok = 0;

Console.WriteLine($"Enviando {options.Count} afectaciones a {options.BusinessUrl} cada {options.IntervalSeconds}s (evento {options.EventId})");
for (var i = 1; i <= options.Count; i++)
{
    var lat = options.MinLat + random.NextDouble() * (options.MaxLat - options.MinLat);
    var lon = options.MinLon + random.NextDouble() * (options.MaxLon - options.MinLon);
    var people = random.Next(10) < 3 ? 0 : (int)Math.Round(Math.Pow(random.NextDouble(), 3) * 900); // la mayoría pocos afectados
    var body = new
    {
        eventId = options.EventId,
        latitude = Math.Round(lat, 6),
        longitude = Math.Round(lon, 6),
        severity = (string?)null, // la deriva Business (RN-04)
        affectedPeople = people,
        municipality = municipalities[random.Next(municipalities.Length)],
        department = (string?)null,
        description = $"Reporte automático #{i} del simulador",
        reportedAt = (DateTimeOffset?)null,
        source = "DEVICE",
    };

    try
    {
        using var response = await http.PostAsJsonAsync("/api/v1/impacts", body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            ok++;
            Console.WriteLine($"[{i}/{options.Count}] 201 ({lat:F5}, {lon:F5}) personas={people}");
        }
        else
        {
            Console.WriteLine($"[{i}/{options.Count}] {(int)response.StatusCode}: {text}");
        }
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"[{i}/{options.Count}] sin conexión con Business: {ex.Message}");
    }

    if (i < options.Count) await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds));
}

Console.WriteLine($"Listo: {ok}/{options.Count} afectaciones recibidas por Business.");
return ok == options.Count ? 0 : 1;

internal sealed record SimulatorOptions(Guid EventId, int Count, double IntervalSeconds,
    double MinLat, double MinLon, double MaxLat, double MaxLon, string BusinessUrl)
{
    public static SimulatorOptions? Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2) map[args[i]] = args[i + 1];

        string Get(string key, string env, string fallback) =>
            map.GetValueOrDefault(key) ?? Environment.GetEnvironmentVariable(env) ?? fallback;

        if (!Guid.TryParse(Get("--event-id", "SIM_EVENT_ID", ""), out var eventId)) return null;
        var inv = CultureInfo.InvariantCulture;
        var bbox = Get("--bbox", "SIM_BBOX", "1.10,-76.70,1.20,-76.60").Split(',').Select(v => double.Parse(v, inv)).ToArray();
        if (bbox.Length != 4) return null;

        return new SimulatorOptions(
            eventId,
            int.Parse(Get("--count", "SIM_COUNT", "20"), inv),
            double.Parse(Get("--interval", "SIM_INTERVAL", "2"), inv),
            bbox[0], bbox[1], bbox[2], bbox[3],
            Get("--business-url", "SIM_BUSINESS_URL", "http://localhost:5002"));
    }
}
