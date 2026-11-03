// Tier 3 - Datos (dueño: Rol 2).
// Esqueleto del día 0: compila con el contrato gRPC (DisasterMaps.Contracts.Data.V1.DisasterData.DisasterDataBase).
// Rol 2 implementa el servicio y lo registra con app.MapGrpcService<...>().
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGet("/", () => "DisasterMaps.Data: los endpoints gRPC requieren un cliente gRPC.");

app.Run();
