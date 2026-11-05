# DisasterMaps — Taller Mapas de Desastres

Aplicación distribuida en 3 capas físicas sobre **.NET 10 (antes .NET Core)** para gestionar afectaciones de desastres y ubicarlas en un mapa.

| Tier | Proyecto | Puerto | Protocolo expuesto |
|---|---|---|---|
| 1 Presentación | `src/DisasterMaps.Web` (MVC + Leaflet) | 5001 | HTTP (HTML / GeoJSON) |
| 2 Negocio | `src/DisasterMaps.Business` (Web API) | 5002 | REST `/api/v1` |
| 3 Datos | `src/DisasterMaps.Data` (gRPC) | 5003 | gRPC `DisasterData` (HTTP/2) |
| EIS | PostgreSQL 16 (Docker) | 5432 | — |
| — | `src/DisasterMaps.Simulator` (consola) | — | cliente REST (dispositivo de campo) |

- Contratos: `contracts/protos/disaster_data.proto` (gRPC) y `contracts/rest/api-v1.md` (REST). Datos de ejemplo en `contracts/samples/`.
- Documentación: `docs/ARQUITECTURA.md` y `docs/DECISIONS.md`.

## Requisitos
- SDK de .NET 10 (`dotnet --list-sdks`)
- Docker Desktop (para PostgreSQL y `docker compose`)

## Ejecutar con Docker (modo demo)
```bash
docker compose up --build           # abre http://localhost:5001
```
Solo la Web publica puerto. Para desarrollo (Business con Scalar en http://localhost:5002/scalar/v1 y Postgres en 5432):
```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

## Ejecutar cada proyecto en local
```bash
docker run -d --name disastermaps-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=disastermaps -p 5432:5432 postgres:16

dotnet run --project src/DisasterMaps.Data       # http://localhost:5003 (gRPC)
dotnet run --project src/DisasterMaps.Business   # http://localhost:5002 (REST; Scalar en /scalar/v1)
dotnet run --project src/DisasterMaps.Web        # http://localhost:5001 (mapa)
```
En Development, Business usa un tier de Datos falso en memoria (`DataTier:UseFake=true` en `appsettings.Development.json`). Para conectarlo a Data real:
```bash
dotnet run --project src/DisasterMaps.Business --DataTier:UseFake=false
```
`src/DisasterMaps.Business/DisasterMaps.Business.http` tiene un ejemplo por endpoint.

## Simulador de dispositivo
```bash
# local
dotnet run --project src/DisasterMaps.Simulator -- --event-id <uuid> --count 20 --interval 2 \
    --bbox 1.10,-76.70,1.20,-76.60 --business-url http://localhost:5002
# dentro de compose
docker compose --profile tools run --rm simulator --event-id <uuid> --count 20 --interval 2
```

## App móvil (operación desconectada)
App Flutter en `mobile/`. Captura latitud, longitud y nivel de afectación (1 a 5) sin conexión, y sincroniza con `POST /api/v1/sync/batch` al recuperarla. Instalación y guion de prueba en `mobile/README.md`; diseño en la sección 10 de `docs/ARQUITECTURA.md`.

```bash
cd mobile && flutter test && flutter build apk --release
# Comparar las dos estrategias de carga por lotes (DAL y base de datos) contra PostgreSQL:
pwsh scripts/benchmark-ingesta.ps1
```

## Pruebas
```bash
dotnet test DisasterMaps.sln
```

## Guion de demo
1. `docker compose up --build` → `docker compose ps` muestra los 4 contenedores `healthy`.
2. Abrir http://localhost:5001 → el mapa muestra los eventos semilla.
3. Crear un evento nuevo (pantalla Eventos).
4. Registrar una afectación haciendo clic en el mapa (**recibir** coordenada).
5. Importar `contracts/samples/impacts-sample.csv` → reporte 17 aceptadas / 3 rechazadas (líneas 5, 10 y 16) y los puntos aparecen (**leer** coordenadas).
6. Correr el simulador → aparecen puntos nuevos en vivo (refresco cada 15 s):
   `docker compose --profile tools run --rm simulator --event-id <id del evento> --count 15 --interval 1`
7. Filtrar por severidad y por evento; abrir el resumen por municipio.
8. `docker compose stop data` → la Web muestra el mensaje amigable y Business responde 503; `docker compose start data` → se recupera sin reiniciar nada más.

## Medir las metas (sección 7 de ARQUITECTURA.md)
Todo de una vez (verificación end-to-end + metas de rendimiento; con `STOP_DATA=1` también la degradación):
```bash
scripts/verify.sh http://localhost:5002
STOP_DATA=1 scripts/verify.sh http://localhost:5002   # con compose en modo dev
```
O a mano, con el modo dev levantado (Business publicado en 5002):
```bash
# Mapa: tiempo de GET /impacts/geojson (objetivo < 1 s con 2.000 afectaciones)
curl -s -o /dev/null -w "%{http_code} %{time_total}s\n" "http://localhost:5002/api/v1/impacts/geojson"

# Importación de 1.000 filas válidas (objetivo < 3 s end-to-end)
python - <<'EOF'
import random
with open("impacts-1000.csv", "w", encoding="utf-8") as f:
    f.write("latitude,longitude,severity,affected_people,municipality,department,description,reported_at\n")
    for i in range(1000):
        f.write(f"{random.uniform(1.0,1.3):.6f},{random.uniform(-76.8,-76.5):.6f},,{random.randint(0,900)},Mocoa,Putumayo,carga {i},2026-10-01T{i//60%24:02d}:{i%60:02d}:00Z\n")
EOF
curl -s -w "\n%{http_code} %{time_total}s\n" -F "file=@impacts-1000.csv" -F "eventId=<uuid>" http://localhost:5002/api/v1/impacts/import
docker compose logs business | grep "Importación"     # tiempo medido dentro de Business

# Degradación: 503 en ≤ 3,5 s con Data detenido
docker compose stop data
curl -s -o /dev/null -w "%{http_code} %{time_total}s\n" http://localhost:5002/api/v1/events
docker compose start data

# Modificabilidad: cambiar Severity:High en appsettings.json (o la variable Severity__High) sin recompilar
```
