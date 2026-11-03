# DisasterMaps — Taller Mapas de Desastres

Aplicación distribuida en 3 capas físicas sobre **.NET 10 (antes .NET Core)** para gestionar afectaciones de desastres y ubicarlas en un mapa.

| Tier | Proyecto | Puerto | Protocolo expuesto |
|---|---|---|---|
| 1 Presentación | `src/DisasterMaps.Web` (MVC + Leaflet) | 5001 | HTTP (HTML / GeoJSON) |
| 2 Negocio | `src/DisasterMaps.Business` (Web API) | 5002 | REST `/api/v1` |
| 3 Datos | `src/DisasterMaps.Data` (gRPC) | 5003 | gRPC `DisasterData` (HTTP/2) |
| EIS | PostgreSQL 16 (Docker) | 5432 | — |

Contratos: `contracts/protos/disaster_data.proto` (gRPC) y `contracts/rest/api-v1.md` (REST). Datos de ejemplo en `contracts/samples/`.

## Requisitos
- SDK de .NET 10 (`dotnet --list-sdks`)
- Docker Desktop (para PostgreSQL y `docker compose`)

## Compilar y probar
```bash
dotnet build DisasterMaps.sln
dotnet test DisasterMaps.sln
```

## Ejecutar cada proyecto en local
```bash
# (Rol 2) Base de datos
docker run -d --name disastermaps-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=disastermaps -p 5432:5432 postgres:16

dotnet run --project src/DisasterMaps.Data       # http://localhost:5003 (gRPC)
dotnet run --project src/DisasterMaps.Business   # http://localhost:5002 (REST)
dotnet run --project src/DisasterMaps.Web        # http://localhost:5001 (mapa)
```

Documentación: `docs/ARQUITECTURA.md` y `docs/DECISIONS.md`.
