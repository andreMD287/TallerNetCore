# Contrato REST v1 — Tier de Negocio (`DisasterMaps.Business`)

> Contrato congelado v1. Dueño: Rol 1. Cualquier cambio se acuerda con Rol 2 antes del push.
> Base local: `http://localhost:5002` · en compose: `http://business:5002`.

## 1. Endpoints `/api/v1`

| Método | Ruta | Uso | Respuesta |
|---|---|---|---|
| GET | `/api/v1/events` | listar eventos | 200 `EventDto[]` |
| POST | `/api/v1/events` | crear evento | 201 `EventDto` |
| GET | `/api/v1/impacts?eventId&eventType&minSeverity&minLat&maxLat&minLon&maxLon&from&to&page&pageSize` | listar afectaciones | 200 `PagedResult<ImpactDto>` |
| GET | `/api/v1/impacts/geojson?` (mismos filtros, sin paginación, tope 2000) | datos para el mapa | 200 `FeatureCollection` |
| GET | `/api/v1/impacts/{id}` | detalle | 200 / 404 |
| POST | `/api/v1/impacts` | **recibir** una coordenada | 201 `ImpactDto` + `Location` |
| PUT | `/api/v1/impacts/{id}` | actualizar | 200 / 404 |
| DELETE | `/api/v1/impacts/{id}` | borrar (soft delete) | 204 / 404 |
| POST | `/api/v1/impacts/import` (multipart: `file`, `eventId`) | **leer** CSV o GeoJSON | 200 `ImportReport` |
| GET | `/api/v1/stats/by-municipality?eventId` | resumen | 200 `MunicipalitySummaryDto[]` |
| GET | `/health` | salud (incluye chequeo de Data) | 200 / 503 |

JSON (camelCase, enums como string, fechas ISO-8601 UTC):
```json
// EventDto
{ "id": "uuid", "name": "Deslizamiento Mocoa", "type": "LANDSLIDE", "startedAt": "2017-04-01T03:00:00Z", "description": "..." }

// ImpactDto
{ "id": "uuid", "eventId": "uuid", "latitude": 1.1522, "longitude": -76.6466,
  "severity": "HIGH", "affectedPeople": 120, "municipality": "Mocoa", "department": "Putumayo",
  "description": "...", "reportedAt": "2026-11-03T15:20:00Z", "source": "MANUAL", "status": "REPORTED" }

// CreateImpactRequest (POST) / UpdateImpactRequest (PUT: mismos campos + status)
{ "eventId": "uuid", "latitude": 1.1522, "longitude": -76.6466,
  "severity": null, "affectedPeople": 120, "municipality": "Mocoa", "department": "Putumayo",
  "description": "...", "reportedAt": null, "source": "MANUAL" }

// PagedResult<T>
{ "items": [ ], "total": 57, "page": 1, "pageSize": 100 }

// FeatureCollection (OJO: GeoJSON es [longitud, latitud])
{ "type": "FeatureCollection", "features": [
  { "type": "Feature", "geometry": { "type": "Point", "coordinates": [-76.6466, 1.1522] },
    "properties": { "id": "uuid", "eventId": "uuid", "severity": "HIGH", "affectedPeople": 120,
                    "municipality": "Mocoa", "department": "Putumayo", "description": "...",
                    "reportedAt": "2026-11-03T15:20:00Z", "source": "MANUAL", "status": "REPORTED" } } ] }

// ImportReport
{ "received": 20, "accepted": 17, "rejected": 3,
  "errors": [ { "line": 5, "reason": "Latitud fuera de rango [-90, 90]" } ] }

// MunicipalitySummaryDto
{ "municipality": "Mocoa", "department": "Putumayo", "impacts": 12, "affectedPeople": 840, "maxSeverity": "CRITICAL" }
```
Errores: `application/problem+json` (RFC 7807, `ProblemDetails`); los de validación usan `ValidationProblemDetails` con `errors: { "latitude": ["..."] }`. Mensajes en español.

### Ejemplo de error de validación (400)
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Uno o más campos no son válidos.",
  "status": 400,
  "errors": {
    "latitude": ["La latitud debe estar en el rango [-90, 90]."]
  },
  "traceId": "00-..."
}
```

## 2. Formatos de importación (`POST /api/v1/impacts/import`)

- **CSV** (UTF-8, separador `,`, punto decimal, con encabezado):
  `latitude,longitude,severity,affected_people,municipality,department,description,reported_at`
  (`severity` y `reported_at` pueden ir vacíos).
- **GeoJSON:** `FeatureCollection` de `Point`; `coordinates = [lon, lat]`; las `properties` usan los mismos nombres que el CSV.
- Se detecta el formato por extensión (`.csv`, `.geojson`, `.json`) y se valida el contenido.
- Límites (RN-06): archivo ≤ 5 MB y ≤ 10.000 filas. Las filas válidas se envían en **un solo lote atómico**; las inválidas se reportan con número de línea y motivo. Si ninguna es válida → 400 con el `ImportReport`.
- Duplicados (RN-07): filas con las mismas coordenadas (5 decimales) y la misma `reported_at` dentro del archivo se reportan como duplicadas.
- Ejemplos: `contracts/samples/impacts-sample.csv` (17 válidas / 3 inválidas) y `contracts/samples/impacts-sample.geojson` (10 válidas).

## 3. Códigos de estado HTTP y su origen

| HTTP | Cuándo | Origen gRPC (Data → Business) |
|---|---|---|
| 400 | Validación de reglas de negocio (RN-01, 03, 05, 06, 10) o dato inválido detectado por Data | `INVALID_ARGUMENT` |
| 404 | Id inexistente o borrado (soft delete) | `NOT_FOUND` |
| 409 | Insert con `id` ya existente (no proveniente de reintento) | `ALREADY_EXISTS` |
| 422 | `eventId` que no existe (RN-02) | `FAILED_PRECONDITION` |
| 503 | Tier de Datos no disponible o deadline excedido | `UNAVAILABLE`, `DEADLINE_EXCEEDED` |
| 500 | Cualquier otro error (sin stack trace) | `INTERNAL` / otros |

## 4. Convención de errores gRPC (Data → Business)

| Situación en Data | Status gRPC | Business lo traduce a |
|---|---|---|
| Id inexistente o borrado (soft delete) | `NOT_FOUND` | 404 |
| Violación de CHECK / dato inválido (red de seguridad) | `INVALID_ARGUMENT` | 400 |
| `event_id` que no existe (FK) | `FAILED_PRECONDITION` | 422 |
| Insert con `id` ya existente | `ALREADY_EXISTS` | si viene de un reintento de `CreateImpact`: éxito (se hace `GetImpact`); si no: 409 |
| BD caída / timeout de conexión | `UNAVAILABLE` | 503 |
| Deadline excedido | `DEADLINE_EXCEEDED` | 503 |
| Cualquier otro | `INTERNAL` (sin stack trace en el mensaje) | 500 |

**Llamadas gRPC:** deadline de 3 s por llamada. Reintento automático (máx. 3 intentos, backoff 0,2 s → 1 s, solo ante `UNAVAILABLE`) únicamente en operaciones idempotentes: lecturas y `CreateImpact` (idempotente porque el `id` lo genera Business). `CreateImpactsBatch`, `UpdateImpact` y `DeleteImpact` sin reintento automático.

## 5. Reglas de negocio aplicadas por este API

- **RN-01** Coordenadas válidas: latitud en [-90, 90], longitud en [-180, 180]; se rechaza exactamente (0, 0) (error típico de coordenada vacía). Se redondean a 6 decimales (~0,1 m).
- **RN-02** Toda afectación pertenece a un evento existente.
- **RN-03** `affectedPeople >= 0` (por defecto 0).
- **RN-04** Severidad: si viene, se respeta; si no, se deriva de `affectedPeople`: 0 → LOW; 1–49 → MEDIUM; 50–499 → HIGH; ≥ 500 → CRITICAL. (Umbrales configurables en `appsettings`.)
- **RN-05** `reportedAt` no puede estar en el futuro (tolerancia 5 min); si no viene, se usa la hora actual UTC.
- **RN-06** Importación: archivo ≤ 5 MB y ≤ 10.000 filas. Se valida fila por fila; las válidas se envían en **un solo lote atómico** y las inválidas se reportan con línea y motivo. Si no hay ninguna válida → 400 con el reporte.
- **RN-07** Dentro de un mismo archivo, filas con las mismas coordenadas (5 decimales) y la misma `reported_at` se reportan como duplicadas.
- **RN-08** El borrado es lógico (`is_deleted`): la afectación deja de aparecer en consultas pero se conserva el histórico.
- **RN-09** `source` lo asigna Business según el canal: formulario → MANUAL, importación → FILE, simulador/dispositivo → DEVICE (el request puede traer DEVICE; MANUAL por defecto).
- **RN-10** Estado inicial siempre REPORTED; transiciones permitidas: REPORTED → VERIFIED → ATTENDED (no se retrocede).
