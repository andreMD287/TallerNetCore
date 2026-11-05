# DisasterMaps — Documento de arquitectura

Taller de Arquitectura de Software (PUJ, 2026-30). Implementación en **.NET 10 (antes .NET Core)**; las lecturas de Jakarta EE son la base conceptual (sección 6).

## 1. Propósito y alcance

> **TALLER.** Con base en las lecturas, implementar una aplicación que permita gestionar los datos de mapas de desastres. Se debe leer o recibir coordenadas de afectaciones y trazar o al menos ubicar las coordenadas en un mapa.

| Requisito derivado | Cómo se cumple |
|---|---|
| **Gestionar** datos de mapas de desastres | CRUD de eventos y afectaciones (`/api/v1/events`, `/api/v1/impacts`) |
| **Leer** coordenadas | Importación de archivos CSV y GeoJSON (`POST /api/v1/impacts/import`) |
| **Recibir** coordenadas | `POST /api/v1/impacts` desde el formulario o desde un dispositivo (simulador) |
| **Trazar / ubicar** en un mapa | Mapa Leaflet + OpenStreetMap con marcadores por severidad, filtros y popups |
| **Recolectar** latitud, longitud y nivel de afectación (1 a 5) **sin conexión** y **sincronizar** al recuperarla | App móvil Flutter con SQLite local + `POST /api/v1/sync/batch` + carga por lotes en Data (sección 10) |

Temas del tablero evidenciados: **ADO.NET, LINQ y Entity Framework** (tier de Datos, sección 3.4) y una arquitectura **3-tier distribuida** con **REST** y **gRPC** (secciones 3 y 5).

## 2. Vista de contexto

```mermaid
flowchart LR
  OP([Operador]) -- navegador HTTP --> SYS[DisasterMaps]
  DEV([Dispositivo de campo / Simulador]) -- REST JSON --> SYS
  APP([Evaluador de campo con la app móvil]) -- REST JSON al recuperar conexión --> SYS
  SYS -- tiles del mapa vía navegador --> OSM[(OpenStreetMap)]
  APP -- tiles del mapa cuando hay red --> OSM
```

| Actor / sistema | Interacción |
|---|---|
| Operador | Consulta el mapa, registra afectaciones (clic en el mapa), importa archivos, cambia estados, ve el resumen por municipio |
| Dispositivo / simulador | Envía afectaciones por REST con `source = DEVICE` |
| Evaluador de campo (app móvil) | Captura posición y nivel de afectación sin conexión; la app sincroniza por lotes con `source = MOBILE` (sección 10) |
| OpenStreetMap | Sistema externo: provee las teselas del mapa (sin API key, con atribución) |

## 3. Vistas de componentes y despliegue

### 3.1 Vista C&C: tres capas físicas cerradas

Cada tier es un proceso independiente y **solo habla con el tier adyacente**: la Web nunca llama a Datos y el navegador nunca llama a Negocio.

```mermaid
flowchart LR
  B[Navegador] -- HTTP HTML/JSON --> W[Tier 1 Web MVC :5001]
  W -- REST JSON --> N[Tier 2 Business API :5002]
  S[Simulador dispositivo] -- REST JSON --> N
  N -- gRPC Protobuf HTTP2 --> D[Tier 3 Data gRPC :5003]
  D -- EF Core / LINQ / ADO.NET --> P[(PostgreSQL :5432)]
```

| Tier | Proyecto | Expone | Consume |
|---|---|---|---|
| 1 Presentación | `DisasterMaps.Web` (ASP.NET Core MVC + Leaflet) | HTML + `/map/data` (GeoJSON) | REST de Business |
| 2 Negocio | `DisasterMaps.Business` (ASP.NET Core Web API) | REST `/api/v1` | gRPC de Data |
| 3 Datos | `DisasterMaps.Data` (ASP.NET Core gRPC) | gRPC `DisasterData` | PostgreSQL |
| EIS | PostgreSQL 16 | SQL | — |

### 3.2 Tier de Negocio (estructura interna)

```mermaid
flowchart TB
  subgraph Business[DisasterMaps.Business]
    API[Api/ controladores REST] --> APP[Application/ EventService, ImpactService, ImportService]
    APP --> VAL[Validation/ + Domain/ reglas RN-01..RN-10]
    APP --> IMP[Import/ parsers CSV y GeoJSON]
    APP --> GW[[DataGateway/ IDataGateway - fachada]]
    GW --> GRPC[GrpcDataGateway]
    GW -.Development.-> FAKE[FakeDataGateway en memoria]
    ERR[Errors/ RpcException a ProblemDetails]
  end
  GRPC -- gRPC --> DATA[(Tier de Datos)]
```

- **Fachada `IDataGateway`:** los servicios nunca usan el cliente gRPC; la fachada trabaja con DTOs y solo `GrpcDataGateway` conoce los mensajes del `.proto`. Con `DataTier:UseFake=true` se inyecta `FakeDataGateway`, que imita el contrato y sus errores. Así Business se desarrolló y probó en paralelo con Data.
- **Servicios sin estado** registrados como *scoped* en el contenedor de inyección de dependencias (equivalente a un `@Stateless`).
- **Reglas de negocio** RN-01..RN-10 en `Domain/` y `Validation/`; los umbrales de severidad se leen con `IOptionsMonitor` (se cambian en `appsettings` sin recompilar).
- **Errores:** `IExceptionHandler` global → `application/problem+json` en español; la validación responde `ValidationProblemDetails` con `errors` por campo.

### 3.3 Vista de despliegue

```mermaid
flowchart LR
  subgraph Host[Máquina host]
    USER[Navegador] -- ":5001 (único puerto publicado)" --> web
    subgraph Compose[docker compose · red interna]
      web[web<br/>aspnet:10.0] -- "http://business:5002" --> business[business<br/>aspnet:10.0]
      sim[simulator<br/>runtime:10.0<br/>perfil tools] -. "http://business:5002" .-> business
      business -- "h2c http://data:5003" --> data[data<br/>aspnet:10.0]
      data -- "postgres:5432" --> pg[(postgres:16<br/>volumen pgdata)]
    end
  end
```

| Contenedor | Imagen | Puerto | Healthcheck | Depende de (sano) |
|---|---|---|---|---|
| `postgres` | `postgres:16` | 5432 (interno) | `pg_isready` | — |
| `data` | build multi-stage | 5003 h2c (interno) | conexión TCP | `postgres` |
| `business` | build multi-stage | 5002 (interno; publicado solo en modo dev) | `GET /alive` | `data` |
| `web` | build multi-stage | **5001 publicado** | conexión TCP | `business` |
| `simulator` | build multi-stage | — | — | `business` (perfil `tools`) |

Las direcciones de los servicios y la cadena de conexión se inyectan por variables de entorno (`DataTier__Address`, `Business__BaseUrl`, `ConnectionStrings__DisasterMaps`).

### 3.4 Tier de Datos

El proceso independiente `DisasterMaps.Data` expone `DisasterData` (los nueve RPC del contrato congelado) por gRPC sobre HTTP/2 sin TLS (`h2c`, puerto 5003) y accede exclusivamente a PostgreSQL 16. En ASP.NET Core se inyecta un mismo `NpgsqlDataSource` para los contextos de EF Core y las operaciones ADO.NET. Los identificadores UUID son asignados por Business.

```mermaid
erDiagram
  DISASTER_EVENTS ||--o{ IMPACTS : posee
  DISASTER_EVENTS {
    uuid id PK
    varchar name
    varchar type
    timestamptz started_at
    text description
  }
  IMPACTS {
    uuid id PK
    uuid event_id FK
    double latitude
    double longitude
    varchar severity
    int affected_people
    varchar municipality
    varchar department
    text description
    timestamptz reported_at
    varchar source
    varchar status
    bool is_deleted
  }
```

**Protección de datos:** `CHECK latitude BETWEEN -90 AND 90`, `CHECK longitude BETWEEN -180 AND 180`, `CHECK NOT(latitude=0 AND longitude=0)`, `CHECK affected_people>=0`, dominio de severidad (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`) y de estado; FK `impacts.event_id → disaster_events.id`. Índices `event_id`, `(latitude,longitude)` y `reported_at`. `IsDeleted` aplica el filtro global de EF para que los registros borrados lógicamente no aparezcan. Timestamps `timestamptz` en UTC. Migración `InitialCreate` y semilla de 40 puntos sintéticos en Development (no son datos oficiales).

**Tres accesos a datos:** EF Core (`DbContext`, `SaveChangesAsync`) administra altas, consultas por ID, modificaciones y soft delete; LINQ compone filtros tipados sobre `IQueryable` con `AsNoTracking`, `Where`, `OrderBy`, `Skip/Take`, `CountAsync` y `GroupBy/Count/Sum/Max` para informes; ADO.NET (`NpgsqlBinaryImporter`, PostgreSQL `COPY ... FORMAT BINARY` dentro de una transacción) gestiona importaciones masivas atómicas sin change tracking. Las validaciones primarias están en Business y las restricciones SQL son la última defensa.

Consultas SQL **representativas** de las expresiones LINQ; el SQL exacto debe capturarse con `ToQueryString()` en la ejecución local (no se declara medido todavía):

```sql
-- ListImpacts: EF agrega WHERE según filtros activos y paginación.
SELECT i.* FROM impacts AS i
WHERE NOT i.is_deleted AND i.latitude >= @min_lat AND i.latitude <= @max_lat
  AND i.longitude >= @min_lon AND i.longitude <= @max_lon
ORDER BY i.reported_at DESC, i.id
LIMIT @page_size OFFSET @offset;
-- SummaryByMunicipality: evaluación del agrupamiento en PostgreSQL.
SELECT municipality, department, COUNT(*) AS impacts,
       SUM(affected_people) AS affected_people,
       MAX(CASE severity WHEN 'CRITICAL' THEN 4 WHEN 'HIGH' THEN 3
           WHEN 'MEDIUM' THEN 2 ELSE 1 END) AS max_severity_rank
FROM impacts WHERE NOT is_deleted
GROUP BY municipality, department;
```

**Errores gRPC:** `ALREADY_EXISTS` para UUID duplicado (idempotencia de reintentos), `FAILED_PRECONDITION` por evento inexistente, `NOT_FOUND` para ID inexistente o soft-deleted; `INVALID_ARGUMENT` por CHECK o validación; `UNAVAILABLE` si falla PostgreSQL; `INTERNAL` sin exponer stack. El health check gRPC `grpc.health.v1` incorpora conectividad a PostgreSQL.

### 3.5 Tier de Presentación

`DisasterMaps.Web` es un proceso independiente ASP.NET Core MVC con vistas Razor en español (puerto 5001); representa el tier Web/JSF de las lecturas. Incluye seis pantallas: mapa de afectaciones, lista paginada de afectaciones (edición de estado y borrado lógico), registro manual con coordenadas desde clic en mapa, importación CSV/GeoJSON con reporte, gestión de eventos y resumen por municipio. Los formularios usan validaciones del lado servidor y muestran `errors` por campo que retorna Business.

El flujo del mapa conserva el cierre entre tiers: **navegador → `GET /map/data` de Web → `GET /api/v1/impacts/geojson` de Business → gRPC Data → PostgreSQL**. El navegador no accede directamente a Business ni a Data. Leaflet 1.9 usa teselas OpenStreetMap con atribución y `L.geoJSON`, respetando el orden GeoJSON `[longitud,latitud]`; marcadores por severidad con leyenda textual, filtros, popups, actualización cada 15 s y clustering sobre 500 puntos. El cliente HTTP tipado usa `Business:BaseUrl` inyectado por configuración y timeout de 5 s; existe cliente simulado para trabajo en paralelo.

Los fallos REST y las desconexiones se muestran con el aviso «No fue posible conectarse con el servicio. Intenta de nuevo en unos segundos.» sin generar una página 500. La importación maneja el caso de `400 ImportReport` (todas las filas rechazadas). En la edición, solo se ofrece la transición permitida `REPORTED → VERIFIED → ATTENDED`.

> **Evidencias pendientes antes de afirmar cumplimiento de metas:** compilar y ejecutar los servicios en compose, importar 17/3, comprobar health, medir SQL emitido y tiempos de 2.000 puntos / 1.000 filas.

## 4. Diagramas de secuencia

### 4.1 Recibir una afectación por REST hasta la BD

```mermaid
sequenceDiagram
  autonumber
  participant C as Web / Simulador
  participant B as Business :5002
  participant D as Data :5003
  participant P as PostgreSQL
  C->>B: POST /api/v1/impacts (JSON)
  B->>B: Validar RN-01, 03, 05 · redondear a 6 decimales<br/>derivar severidad (RN-04) · source (RN-09) · status REPORTED
  B->>B: id = Guid.CreateVersion7()
  B->>D: gRPC CreateImpact(Impact) · deadline 3 s · reintento ante UNAVAILABLE
  D->>P: INSERT (EF Core)
  alt evento inexistente
    P-->>D: violación de FK
    D-->>B: FAILED_PRECONDITION
    B-->>C: 422 problem+json
  else reintento de un insert que ya se hizo
    D-->>B: ALREADY_EXISTS
    B->>D: GetImpact(id)
    D-->>B: Impact (mismo contenido)
    B-->>C: 201 Created
  else éxito
    D-->>B: ImpactReply
    B-->>C: 201 Created + Location + ImpactDto
  end
```

### 4.2 Importar un archivo con filas inválidas

```mermaid
sequenceDiagram
  autonumber
  participant W as Web
  participant B as Business
  participant D as Data
  participant P as PostgreSQL
  W->>B: POST /api/v1/impacts/import (multipart: file, eventId)
  B->>B: ≤ 5 MB · parser por extensión (.csv / .geojson)
  loop cada fila (≤ 10.000)
    B->>B: validar RN-01/03/05 · duplicados RN-07<br/>válida → lote · inválida → error(línea, motivo)
  end
  alt ninguna válida
    B-->>W: 400 ImportReport
  else hay válidas
    B->>D: gRPC CreateImpactsBatch(válidas) · sin reintento
    D->>P: COPY binario (ADO.NET) en una transacción
    D-->>B: BatchReply(inserted)
    B-->>W: 200 ImportReport {received 20, accepted 17, rejected 3, errors[...]}
  end
```

### 4.3 Data caído → 503 → mensaje amigable

```mermaid
sequenceDiagram
  autonumber
  participant U as Navegador
  participant W as Web
  participant B as Business
  participant D as Data (detenido)
  U->>W: GET /map/data
  W->>B: GET /api/v1/impacts/geojson
  B-xD: gRPC ListImpacts
  Note over B,D: UNAVAILABLE → hasta 3 intentos con backoff,<br/>todo dentro del deadline de 3 s
  B-->>W: 503 problem+json "El servicio de datos no está disponible..."
  W-->>U: Página con "No fue posible conectarse con el servicio. Intenta de nuevo en unos segundos." (nunca 500)
  Note over D: docker start data
  U->>W: Actualizar
  W->>B: GET /api/v1/impacts/geojson
  B->>D: gRPC ListImpacts (el canal se reconecta solo)
  D-->>B: ListImpactsReply
  B-->>W: 200 FeatureCollection
```

## 5. Decisiones de arquitectura

| ID | Driver | Alternativas | Decisión y trade-off |
|---|---|---|---|
| **D-01** Capas físicas cerradas | Enunciado (3-tier distribuida); separación de responsabilidades | (a) monolito en capas lógicas; (b) 3 procesos con acceso solo al adyacente | (b). **Mejora** modificabilidad, despliegue independiente y aislamiento (el navegador no ve Business ni Data). **Degrada** latencia (2 saltos de red) y complejidad operativa (4 contenedores). |
| **D-02** REST arriba, gRPC abajo | Interoperabilidad hacia clientes; eficiencia interna | (a) REST en ambos; (b) gRPC en ambos; (c) REST Web→Business y gRPC Business→Data | (c). REST es legible, documentable con OpenAPI y consumible por cualquier cliente (gRPC no corre en el navegador sin gRPC-Web). gRPC da un contrato fuerte generado desde `.proto` y binario sobre HTTP/2 para lotes grandes. **Costo:** dos tecnologías y dos contratos; depurar gRPC exige grpcurl/Postman. |
| **D-03** Tres tecnologías de acceso a datos | Enunciado (ADO.NET, LINQ, EF) y rendimiento | Una sola tecnología vs cada una donde aporta | EF Core para el CRUD, LINQ sobre `IQueryable` para filtros y agregados, ADO.NET (COPY) para la importación masiva. **Costo:** el lote ADO.NET se salta las validaciones de EF → la validación vive en Business y las CHECK de la BD son la última barrera. |
| **D-04** Ids generados en Business (UUID v7) | Confiabilidad: reintentos seguros | (a) id autogenerado por la BD; (b) id del cliente | (b). Hace `CreateImpact` **idempotente**: un reintento tras un fallo de red no duplica la afectación (ALREADY_EXISTS → se resuelve con `GetImpact`). UUID v7 es ordenable por tiempo, lo que favorece al índice. **Costo:** ids de 16 bytes. |
| **D-05** Deadline y reintentos gRPC | Disponibilidad: degradar rápido | Sin límites vs deadline + retry selectivo | Deadline de 3 s por llamada; reintento (3 intentos, backoff 0,2→1 s) **solo** en lecturas y `CreateImpact`, ante UNAVAILABLE. Lote, update y delete no se reintentan (no son idempotentes). Garantiza el 503 en ≤ 3,5 s. |
| **D-06** Fachada `IDataGateway` + fake | Modificabilidad y trabajo en paralelo | Usar el cliente gRPC directo | Fachada con dos implementaciones (gRPC / en memoria). Permitió desarrollar y probar Business sin Data. **Costo:** una capa de mapeo más. |
| **D-07** Leaflet + OpenStreetMap | Sin costo ni API key; enunciado pide ubicar en mapa | Google Maps, Mapbox, OpenLayers | Leaflet 1.9 por CDN + tiles OSM con atribución. Ligero y suficiente para puntos. **Costo:** dependencia de un servicio externo de tiles. |
| **D-08** Errores RFC 7807 en español | Usabilidad e interoperabilidad | Formato propio | `ProblemDetails` / `ValidationProblemDetails` con `errors` por campo y `traceId` para correlacionar con los logs. |

El detalle de las decisiones menores está en `docs/DECISIONS.md` (DEC-xx).

## 6. Equivalencias con las lecturas (Jakarta EE → .NET)

| Concepto de las lecturas | En esta solución | Lectura de referencia |
|---|---|---|
| Multitier distribuido JEE (client / web / business / EIS tier) | Web :5001 / Business :5002 / Data :5003 / PostgreSQL | Clase 1 Intro Jakarta EE; Goncalves cap. 1 |
| Ejercicio B de EJB: negocio y datos en dos WildFly (8080 / 8090) | Business y Data como dos procesos (5002 / 5003) | Clase 3 EJB |
| EJB `@Remote` + RMI-IIOP, clientes remotos | servicio gRPC definido en `.proto` | Clase 3 EJB; Wetherbee caps. 2 y 12 |
| EJB `@Stateless` | servicios de aplicación sin estado registrados en el contenedor DI (scoped) | Wetherbee cap. 2; Goncalves cap. 7; Späth cap. 7 |
| Fachada de bajo acoplamiento negocio↔datos | `IDataGateway` en Business (fachada del cliente gRPC) | Clase 3 EJB |
| CDI / JNDI lookup | inyección de dependencias + `appsettings` (direcciones de servicios) | Goncalves cap. 2; Wetherbee cap. 10 |
| JPA `EntityManager` / contexto de persistencia | EF Core `DbContext` / change tracker | Clase 2 JPA; Jungmann cap. 6 |
| `@Entity` POJO con `@Id`, `@OneToMany` | entidad POCO + Fluent API (`DisasterEvent` 1—N `Impact`) | Jungmann caps. 4–5; Goncalves cap. 5 |
| JPQL / Criteria API (consultas dinámicas) | LINQ con `IQueryable` componible | Jungmann caps. 7–9 |
| JDBC | ADO.NET (Npgsql) | Jungmann cap. 1 |
| Transacciones JTA | transacción explícita en el lote ADO.NET / `SaveChanges` | Späth cap. 10; Goncalves cap. 9 |
| Bean Validation | validadores en Business + data annotations en la Web | Goncalves cap. 3; Bauke cap. 5 |
| JAX-RS / Jakarta REST + JSON | ASP.NET Core Web API | Späth cap. 5; Goncalves cap. 15 |
| JSF/Faces: FacesServlet, backing beans, Facelets (MVC) | ASP.NET Core MVC: routing, controllers, vistas Razor (+ layout) | Clase 5 JSF; Bauke caps. 7–8; especificación Faces 4.1 |
| JMS (cola punto-a-punto) / Message-Driven Beans | no se usa; extensión diferida para ingesta asíncrona (sección 8) | Clase 3a JMS; Wetherbee cap. 5 |

## 7. Metas de verificación

| Atributo | Estímulo / entorno | Medida objetivo | Resultado medido |
|---|---|---|---|
| Rendimiento (mapa) | 2.000 afectaciones en BD, compose local | `GET /impacts/geojson` < 1 s; el mapa las pinta en < 2 s | _pendiente: requiere el despliegue completo con PostgreSQL_ |
| Rendimiento (importación) | CSV de 1.000 filas válidas | end-to-end < 3 s | _pendiente: requiere el despliegue completo con PostgreSQL_. Con el tier de Datos simulado: 20 filas procesadas en Business en 15 ms |
| Disponibilidad / degradación | contenedor Data detenido | Business responde 503 en ≤ 3,5 s; la Web muestra un mensaje amigable; se recupera sin reiniciar | Prueba automática `DataTierDownTests`: **503 en ~3 s** con el cliente gRPC real contra un puerto cerrado. Recuperación en compose: _pendiente_ |
| Modificabilidad | cambiar los umbrales de severidad | solo `appsettings`, sin recompilar | **Cumple:** `SeverityPolicy` lee `IOptionsMonitor<SeverityThresholds>` (prueba `Uses_current_thresholds_when_configuration_changes`) |

Cómo medir: ver la sección "Medir las metas" del `README.md`.

## 8. Diferidos (fuera de alcance, con justificación)

- **Seguridad (autenticación y autorización):** el enunciado no la pide. Mitigación mínima: solo publican puerto la Web y Business (este último para la app móvil, DEC-16); Data y PostgreSQL quedan en la red interna de Docker.
- **PostGIS / consultas espaciales:** lat/lon como `double` + índice compuesto bastan para filtrar por bbox con el volumen del taller (miles de puntos).
- **Mensajería asíncrona (estilo JMS / RabbitMQ)** para dispositivos de campo: la ingesta es síncrona por REST; una cola desacoplaría los picos de reportes y es la extensión natural (equivalente a un Message-Driven Bean).
- **Polígonos de zonas afectadas:** solo puntos; usar GeoJSON deja la puerta abierta.

## 9. Cómo ejecutar

```bash
# Todo con Docker (modo demo): abre http://localhost:5001
docker compose up --build

# Simulador de dispositivo (en otra terminal)
docker compose --profile tools run --rm simulator --event-id <uuid> --count 20 --interval 2

# Modo dev: además Business en http://localhost:5002/scalar/v1 y Postgres en 5432
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build

# Pruebas
dotnet test DisasterMaps.sln
```

Más detalle (ejecución de cada tier por separado y guion de demo) en el `README.md`.

## 10. Módulo de operación desconectada (app móvil)

> **Enunciado.** Crear una aplicación móvil que permita recolectar información de posición (latitud y longitud) y el nivel de afectación (1, 2, 3, 4 o 5) de los sitios de desastre. La aplicación debe funcionar de manera desconectada cuando no se cuente con acceso a internet. Una vez se pueda reestablecer la comunicación, se debe sincronizar la información en el sistema central.

Trazabilidad con el SRS del proyecto: RF-ED-11 (descargar antes la información para operar sin conexión), RF-ED-12 (registrar sin conexión y guardar en el dispositivo), RF-ED-13 (sincronizar al recuperar la conexión, contemplando conflictos), CON-13 (apps móviles funcionan parcialmente sin conexión), CRN-01 (operación desconectada y reconciliación) y QA-REC-02 (no perder lo capturado localmente).

### 10.1 Drivers y escenarios de calidad

| ID | Fuente → estímulo | Respuesta esperada | Medida | Cómo se cumple |
|---|---|---|---|---|
| ESC-M1 (QA-REC-02) | Evaluador sin señal guarda 50 reportes y el sistema central está caído | Nada se pierde; todo queda identificable como pendiente | 0 reportes perdidos | SQLite local como fuente de verdad. Solo se marca "Sincronizado" con confirmación del servidor (DEC-15). Prueba `sin conexión no se pierde nada` |
| ESC-M2 (CRN-01-01) | Vuelve la señal con reportes pendientes | Se envían sin intervención del usuario | Inicio ≤ 1 min tras recuperar la red | Escucha de conectividad + temporizador de 1 min + reintento con espera creciente |
| ESC-M3 (idempotencia) | Se pierde la respuesta de un envío y la app reenvía el lote | No hay duplicados en el sistema central | 0 duplicados | `clientId` generado en el teléfono = id definitivo; reenvío → DUPLICATE (DEC-11) |
| ESC-M4 (integridad) | Llega un reporte con el mismo id y otro contenido | No se sobrescribe; queda explícito | 0 sobrescrituras silenciosas | `CONFLICT` en ambas estrategias de carga (SAD 4.8, punto 6) |
| ESC-M5 (rendimiento de la carga) | Llega un lote de 10.000 reportes | Se carga completo | < 2 s en el tier de Datos | **1,2 s** con la estrategia DATABASE (DEC-10) |
| ESC-M6 (usabilidad en campo) | El evaluador no sabe si hay conexión | Siempre ve el estado y cuántos reportes faltan | Visible en todas las pantallas | Franja de conexión arriba + contador en la pestaña "Reportes" |

### 10.2 Vista de componentes

```mermaid
flowchart LR
  subgraph Phone[Teléfono Android · app Flutter]
    UI[Pantallas: Reportar · Reportes · Mapa · Ajustes] --> ST[AppState]
    ST --> REPO[ReportRepository]
    REPO --> SQL[(SQLite: reports, events)]
    ST --> ENG[SyncEngine]
    ENG --> REPO
    ENG --> API[DisasterApi · cliente REST]
    NET[connectivity_plus] -. red recuperada .-> ST
    GPS[geolocator · GPS sin red] --> UI
  end
  API -- "POST /api/v1/sync/batch (REST JSON)" --> SC[SyncController]
  subgraph Business[Tier 2 · Business]
    SC --> SS[SyncService · valida RN-01/03/05 y nivel 1..5]
    SS --> GW[[IDataGateway]]
  end
  GW -- "gRPC IngestImpactsBatch" --> ING
  subgraph Data[Tier 3 · Data]
    ING[ImpactIngestor · valida, parte en paquetes, elige estrategia] --> DAL[DalIngestionStrategy · LINQ + COPY]
    ING --> DBS[DatabaseIngestionStrategy · COPY a staging + función]
  end
  DAL --> PG[(PostgreSQL · impacts)]
  DBS --> STG[(impacts_staging UNLOGGED)] --> FN[[ingest_impacts_from_staging]] --> PG
```

| Elemento | Responsabilidad |
|---|---|
| `SQLite` (teléfono) | Fuente de verdad mientras no hay red. Guarda reportes con su estado (`PENDING`, `SYNCED`, `CONFLICT`, `REJECTED`), intentos, último error y próxima hora de reintento, más los eventos descargados (RF-ED-11) |
| `SyncEngine` | Envía los pendientes en lotes de 25 a 500 registros (configurable), aplica la respuesta por registro y programa la espera ante fallos. Nunca borra un reporte no confirmado |
| `SyncService` (Business) | Valida forma del lote (400) y reglas por registro (rechazo individual). Asigna `source = MOBILE`, deriva la severidad del nivel (DEC-13) y arma el `SyncReport` |
| `ImpactIngestor` (Data) | Red de seguridad de validación, partición en paquetes transaccionales y elección DAL/DATABASE (DEC-10, DEC-12) |

### 10.3 Vista de despliegue

```mermaid
flowchart LR
  subgraph Campo[Zona del desastre]
    T1[Teléfono 1 · APK]
    T2[Teléfono N · APK]
  end
  subgraph Host[Servidor · Docker Compose]
    W[web :5001]
    B[business :5002]
    D[data :5003]
    P[(postgres :5432)]
  end
  T1 -- "HTTP :5002 (Wi-Fi o datos, cuando hay)" --> B
  T2 -- "HTTP :5002" --> B
  W --> B --> D --> P
```

Business publica 5002 para los teléfonos (DEC-16). En el emulador de Android la dirección es `http://10.0.2.2:5002`; en un teléfono real, `http://<IP del servidor>:5002` en la misma red.

### 10.4 Ciclo de vida de un reporte en el teléfono

```mermaid
stateDiagram-v2
  [*] --> PENDING: Guardar (siempre local primero)
  PENDING --> PENDING: sin red / 503 / timeout\n(intentos+1, espera 30 s → 15 min)
  PENDING --> SYNCED: INSERTED o DUPLICATE
  PENDING --> REJECTED: REJECTED (errores por campo)
  PENDING --> CONFLICT: CONFLICT (mismo id, otro contenido)
  REJECTED --> PENDING: Corregir y reenviar (mismo id)
  CONFLICT --> PENDING: Enviar como reporte nuevo (id nuevo)
  REJECTED --> [*]: Eliminar del teléfono
  CONFLICT --> [*]: Descartar
  SYNCED --> [*]: Quitar del teléfono (el dato ya está en el sistema central)
```

### 10.5 Secuencia: capturar sin conexión y sincronizar al volver la señal

```mermaid
sequenceDiagram
  actor E as Evaluador
  participant A as App (SQLite)
  participant B as Business
  participant D as Data
  participant P as PostgreSQL
  Note over A: Sin conexión
  E->>A: GPS + nivel 4 + Guardar
  A->>A: INSERT reports (PENDING, clientId = UUID v7)
  A-->>E: "Guardado en el teléfono"
  Note over A: Vuelve la red (connectivity_plus)
  A->>B: GET /health
  B-->>A: 200
  A->>B: POST /api/v1/sync/batch {batchId, deviceId, items[≤100]}
  B->>B: valida cada registro (RN-01, 03, 05, nivel 1..5)
  B->>D: gRPC IngestImpactsBatch (solo válidos)
  D->>D: < 2000 → DAL · ≥ 2000 → DATABASE
  D->>P: SELECT ids/eventos existentes (LINQ)
  D->>P: COPY nuevos (una transacción por paquete)
  D-->>B: resultado por registro
  B-->>A: 200 SyncReport (INSERTED/DUPLICATE/CONFLICT/REJECTED)
  A->>A: UPDATE status en una transacción
  A-->>E: Franja: "Conectado: todo está sincronizado"
```

Si la respuesta se pierde, el siguiente intento reenvía los mismos `clientId`. Data responde DUPLICATE y la app los marca como sincronizados (ESC-M3).

### 10.6 Carga por lotes: DAL frente a base de datos

| | DAL (`DalIngestionStrategy`) | Base de datos (`DatabaseIngestionStrategy`) |
|---|---|---|
| Quién clasifica | C# con LINQ sobre EF Core | Función PL/pgSQL `ingest_impacts_from_staging` |
| Cómo inserta | `COPY` binario a `impacts` | `COPY` a `impacts_staging` (UNLOGGED) + `INSERT … SELECT … ON CONFLICT DO NOTHING` |
| Transacción | Lectura e inserción separadas; ante una violación de PK, reclasifica el paquete una vez | Clasificación e inserción en la misma sentencia |
| Mejor con | Lotes pequeños: 100 registros en 16 ms frente a 46 ms | Lotes grandes: 10.000 registros en 1,2 s frente a 1,6 s |
| Tecnologías del tablero | LINQ + Entity Framework + ADO.NET | ADO.NET + SQL del motor |

Las dos estrategias conviven y `ImpactIngestor` elige por tamaño (umbral 2.000, configurable). La medición completa y la decisión están en DEC-10, y se reproducen con `scripts/benchmark-ingesta.ps1`.

### 10.7 Modelo de datos agregado

- `impacts.affectation_level smallint NOT NULL CHECK (1..5)`: nivel capturado (DEC-13).
- `impacts.device_id varchar(64)`: teléfono que capturó el reporte.
- `impacts.received_at timestamptz DEFAULT now()`: cuándo lo incorporó la plataforma (T_sys). `reported_at` sigue siendo cuándo ocurrió (T_eff). Así se distinguen las dos preguntas del SAD 4.10: qué pasó en un instante y qué sabía la plataforma en ese instante.
- `impacts_staging` (UNLOGGED) y la función `ingest_impacts_from_staging(uuid)`: solo para la estrategia DATABASE.

Migración: `20261105120000_MobileSyncIngestion`.

### 10.8 Cómo probar el módulo

```bash
docker compose up -d --build                     # Business queda en :5002 para la app
cd mobile && flutter test                        # 13 pruebas de la app (sincronización, coordenadas, modelo)
flutter build apk --release                      # build/app/outputs/flutter-apk/app-release.apk
pwsh scripts/benchmark-ingesta.ps1               # compara DAL y DATABASE contra PostgreSQL
```

Guion de demostración:
1. Instale el APK y, en Ajustes, ponga la dirección del servidor y descargue los eventos.
2. Active el modo avión y registre varios reportes con GPS o tocando el mapa. La franja dice "Sin conexión" y el contador de pendientes sube.
3. Desactive el modo avión. En menos de un minuto los reportes pasan a "Sincronizado" y aparecen en el mapa web con `source = MOBILE`.
