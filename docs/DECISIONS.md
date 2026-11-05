# Registro de decisiones

Decisiones de diseño menores tomadas durante la implementación (las principales están en la sección 5 de `ARQUITECTURA.md`).
Formato: contexto, alternativas, decisión, consecuencias.

## DEC-01 · Fuente de paquetes NuGet fijada en el repo
- **Contexto:** con una instalación nueva del SDK no había fuentes NuGet configuradas y la restauración fallaba.
- **Alternativas:** (a) que cada desarrollador configure su máquina; (b) `nuget.config` en la raíz del repo con `nuget.org`.
- **Decisión:** (b).
- **Consecuencias:** restauración reproducible en cualquier máquina y en Docker; un archivo más en la raíz.

## DEC-02 · Formato de logs de Business
- **Contexto:** la sección 7.2 exige logging estructurado con el TraceId de cada request.
- **Alternativas:** (a) consola JSON nativa (`AddJsonConsole` + scopes); (b) consola de texto con scopes; (c) Serilog.
- **Decisión:** (a). Cada línea es un JSON con `TraceId`, `SpanId` y los parámetros del mensaje.
- **Consecuencias:** sin dependencias extra y fácil de filtrar en `docker logs` (p. ej. por TraceId para seguir una llamada hasta Data); menos legible a simple vista que el texto plano.

## DEC-03 · Interpretación de RN-10 (transiciones de estado)
- **Contexto:** RN-10 dice REPORTED → VERIFIED → ATTENDED sin retroceder; no aclara si se puede saltar un estado.
- **Alternativas:** (a) solo el paso siguiente (o quedarse igual); (b) cualquier avance.
- **Decisión:** (a) paso a paso.
- **Consecuencias:** trazabilidad completa del ciclo de atención; la Web debe ofrecer solo el estado siguiente.

## DEC-04 · Validación de reglas de negocio
- **Alternativas:** (a) validadores escritos a mano; (b) FluentValidation; (c) data annotations.
- **Decisión:** (a). Las reglas de campo viven en `Domain/ImpactRules` y las reutilizan la API y la importación.
- **Consecuencias:** sin dependencias y mensajes en español controlados; un poco más de código.

## DEC-05 · UI de OpenAPI
- **Decisión:** Scalar (`/scalar/v1`) sobre el documento OpenAPI nativo de .NET 10 (`/openapi/v1.json`), activo en Development o con `OpenApi:Enabled=true`.

## DEC-06 · Estructura interna de Business
- **Decisión:** la sugerida en 7.2 (`Api/`, `Application/`, `Domain/`, `Validation/`, `Import/`, `DataGateway/`, `Mapping/`, `Errors/`, `GeoJson/`). La fachada `IDataGateway` trabaja con DTOs; solo `GrpcDataGateway` y `Mapping/ProtoMapping` conocen los mensajes del `.proto`.

## DEC-07 · Detalles de implementación de las reglas (interpretaciones menores)
- **ALREADY_EXISTS en `CreateImpact`:** como el reintento automático de gRPC no informa si hubo reintento, Business hace `GetImpact` y compara el contenido (evento, coordenadas, personas, fecha). Si coincide es el mismo insert → 201; si no → 409.
- **RN-09:** si un POST trae `source = FILE`, se registra como MANUAL (FILE solo lo asigna la importación).
- **RN-06 / número de línea:** en CSV es la línea física (encabezado = 1, las líneas en blanco cuentan); en GeoJSON es el número de feature (base 1).
- **RN-06 / ninguna válida:** 400 con el `ImportReport` como cuerpo (no ProblemDetails), para que la Web pinte la misma tabla de errores.
- **RN-07:** la clave de duplicado usa la fecha ya resuelta; dos filas en el mismo punto y ambas sin `reported_at` se consideran duplicadas.
- **Data caído:** según cuánto tarde el SO en rechazar la conexión, el cliente gRPC termina en UNAVAILABLE o en DEADLINE_EXCEEDED; ambos → 503 con mensaje amigable, siempre dentro del deadline de 3 s.

---

# Módulo de operación desconectada (app móvil) · DEC-10 a DEC-19

## DEC-10 · ¿Quién carga el lote: el DAL o la base de datos?
- **Contexto:** la app envía lo capturado sin conexión en un lote, y el tier de Datos lo carga por paquetes. Hay que clasificar cada registro (nuevo, reenvío, conflicto o evento inexistente) e insertar los nuevos.
- **Alternativas:**
  - (a) **DAL:** el tier de Datos consulta con LINQ (EF Core) qué ids y eventos existen, clasifica en C# e inserta los nuevos con `COPY` binario (ADO.NET).
  - (b) **Base de datos:** el DAL solo copia el paquete crudo con `COPY` a la tabla `UNLOGGED impacts_staging`, y la función PL/pgSQL `ingest_impacts_from_staging` clasifica, inserta con `ON CONFLICT DO NOTHING` y limpia, todo en una sentencia.
  - (c) ambas, elegidas por tamaño del lote.
- **Medición** (`scripts/benchmark-ingesta.ps1`, Docker Desktop en el portátil de desarrollo, paquetes de 500, mediana de 3 repeticiones, tiempo medido dentro del tier de Datos):

  | Registros | DAL nuevos | BD nuevos | DAL reenvío | BD reenvío |
  |---:|---:|---:|---:|---:|
  | 100 | **16 ms** | 46 ms | **6 ms** | 10 ms |
  | 1.000 | 131 ms | **126 ms** | **22 ms** | 40 ms |
  | 5.000 | 588 ms | **542 ms** | **92 ms** | 166 ms |
  | 10.000 | 1.621 ms | **1.196 ms** | 385 ms | **332 ms** |

- **Decisión:** (c).
  - Las dos estrategias están implementadas detrás de `IImpactIngestionStrategy` y dan exactamente el mismo resultado por registro.
  - Si la solicitud no pide una, `ImpactIngestor` usa DAL por debajo de 2.000 registros y DATABASE desde ahí (`Ingestion:AutoThreshold`, configurable). `?strategy=` la fuerza para medir.
- **Consecuencias:**
  - Lotes pequeños (una sincronización típica del teléfono, 100 registros): el DAL evita escribir en la tabla intermedia y llamar a la función, y responde unas 3 veces más rápido.
  - Lotes grandes (importaciones masivas o muchos teléfonos acumulados): la base de datos clasifica con un solo join en vez de traer las filas al proceso, y es un 26 % más rápida con 10.000 registros.
  - DATABASE clasifica e inserta en la misma transacción. En DAL la lectura y el `COPY` van separados, así que si otro proceso inserta el mismo id en medio, el `COPY` falla completo y el paquete se reclasifica una vez.
  - **Costo:** la lógica de clasificación existe dos veces (C# y PL/pgSQL). Las pruebas de ambas estrategias deben dar los mismos resultados.

## DEC-11 · El dispositivo genera la identidad del reporte
- **Contexto:** sin conexión no se puede pedir un id al servidor, y un envío puede llegar sin que el teléfono reciba la respuesta (SAD 4.8: "identidad técnica sin contactar al núcleo").
- **Alternativas:** (a) id asignado por el servidor y deduplicación por contenido; (b) UUID v7 generado en el teléfono (`clientId`) como id definitivo.
- **Decisión:** (b), igual que D-04 pero en el borde. Mismo id y mismo contenido → `DUPLICATE`, y el teléfono lo da por sincronizado. Mismo id con otro contenido → `CONFLICT`, que nunca se sobrescribe. El usuario decide si lo envía como reporte nuevo o lo descarta.
- **Consecuencias:** reenviar es seguro, así que `IngestImpactsBatch` entra en la lista de métodos gRPC que se reintentan (D-05). No se detectan como duplicados dos reportes distintos del mismo sitio; eso es consolidación (SAD 4.9) y queda fuera de este módulo.

## DEC-12 · Carga no atómica, en paquetes transaccionales
- **Contexto:** la importación de archivos es todo o nada (RN-06). Pero en campo, un registro inválido no debe impedir que lleguen los demás.
- **Alternativas:** (a) lote atómico; (b) resultado por registro, con cada paquete de `ChunkSize` (500) en su propia transacción.
- **Decisión:** (b). Business valida cada registro (RN-01, RN-03, RN-05 y nivel 1..5) y solo envía los válidos. La respuesta trae un estado por registro, con `errors` por campo.
- **Consecuencias:** un fallo a mitad del lote deja confirmados los paquetes anteriores. No hay problema, porque reenviar el lote completo los devuelve como `DUPLICATE`. Errores de forma del lote (sin `batchId` o `deviceId`, vacío, más de 10.000 registros) → 400 completo.

## DEC-13 · Nivel de afectación 1..5 junto a la severidad
- **Contexto:** el enunciado pide capturar un nivel de afectación de 1 a 5, pero el sistema ya tenía una severidad de 4 valores que usan la Web, los filtros y el resumen.
- **Alternativas:** (a) reemplazar la severidad por el nivel; (b) convertir el nivel a severidad y no guardarlo; (c) guardar ambos con un mapeo fijo.
- **Decisión:** (c). Nueva columna `affectation_level` (1..5, CHECK). Mapeo en `Domain/AffectationLevels`: 1 → LOW, 2 → MEDIUM, 3 → HIGH, 4 y 5 → CRITICAL. Los registros existentes se migraron con LOW = 1, MEDIUM = 2, HIGH = 3 y CRITICAL = 4.
- **Consecuencias:** nada de lo existente cambia de comportamiento. El mapa de calor interpola el nivel, que tiene más resolución. Los niveles 4 y 5 comparten severidad.

## DEC-14 · Tecnología de la app móvil: Flutter
- **Contexto:** el profesor no exige tecnología, y se pide entregar un APK. El sistema central es .NET.
- **Alternativas:**
  - (a) .NET MAUI: mismo lenguaje que el resto. Pero en Android el mapa usa Google Maps y exige API key, y las opciones para trabajar sin conexión y sincronizar están menos maduras.
  - (b) **Flutter:** APK nativo y SQLite maduro (`sqflite`). `flutter_map` usa OpenStreetMap igual que la Web, el GPS funciona sin red (`geolocator`) y la conectividad se detecta con `connectivity_plus`.
  - (c) PWA: más simple, pero el almacenamiento del navegador puede borrarse y el acceso al GPS en segundo plano es limitado.
- **Decisión:** (b).
- **Consecuencias:** un lenguaje más (Dart) en el repo, en `mobile/`. El contrato con el sistema central es el REST de siempre (D-02), así que la app no depende de la tecnología del servidor.

## DEC-15 · Almacenamiento local y política de sincronización
- **Contexto:** QA-REC-02 exige que no se pierda ninguna evaluación confirmada localmente, y CRN-01 exige reconciliar al recuperar conexión.
- **Decisión:**
  - SQLite es la fuente de verdad del teléfono, y todo se guarda ahí antes de cualquier envío.
  - Un reporte solo pasa a "Sincronizado" con `INSERTED` o `DUPLICATE` del servidor. Los sincronizados no se pueden borrar ni editar desde el teléfono.
  - Se envía al recuperar la red, al volver a la app, cada minuto mientras está abierta, al guardar y con el botón "Sincronizar ahora".
  - Ante un fallo de red o un 503 se espera 30 s, 1, 2, 4… hasta 15 min antes de reintentar.
  - `CONFLICT` y `REJECTED` no se reintentan solos.
- **Alternativa descartada:** sincronización en segundo plano con WorkManager con la app cerrada. Agrega permisos y otro punto de ejecución, y la app abierta al recuperar la señal ya cubre el escenario del taller.
- **Consecuencias:** con la app cerrada no se envía nada hasta que se vuelve a abrir, pero tampoco se pierde nada.

## DEC-16 · Business publica el puerto 5002
- **Contexto:** la app móvil llega a Business desde la red local, y antes solo la Web publicaba puerto (sección 8, seguridad diferida).
- **Decisión:** `docker-compose.yml` publica 5002. Data y PostgreSQL siguen solo en la red interna.
- **Consecuencias:** Business queda expuesto en la red local sin autenticación, igual que la Web. En un despliegue real iría detrás de un gateway con TLS y autenticación de dispositivos.

## DEC-17 · Migraciones SQL escritas a mano y la verificación de EF Core
- **Contexto:** EF Core 10 se niega a migrar si cree que el modelo tiene cambios sin migración (`PendingModelChangesWarning`). Aquí las migraciones son SQL a mano y el snapshot se arma sin convenciones, así que la verificación siempre falla. Con una base vacía, el tier de Datos no arrancaba.
- **Decisión:** ignorar esa advertencia en `AddDbContext`. La fuente de verdad del esquema son las migraciones SQL.
- **Consecuencias:** EF no avisa si alguien cambia el modelo sin escribir la migración. Lo cubren las pruebas de punta a punta contra PostgreSQL.
