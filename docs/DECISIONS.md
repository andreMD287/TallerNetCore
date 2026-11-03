# Registro de decisiones

Cada rol registra aquí las decisiones no escritas en el plan base, con prefijo `R1-` (Rol 1) o `R2-` (Rol 2).
Formato: contexto, alternativas, decisión, consecuencias.

## R1-01 · Fuente de paquetes NuGet fijada en el repo
- **Contexto:** con una instalación nueva del SDK no había fuentes NuGet configuradas y la restauración fallaba.
- **Alternativas:** (a) que cada integrante configure su máquina; (b) `nuget.config` en la raíz del repo con `nuget.org`.
- **Decisión:** (b).
- **Consecuencias:** restauración reproducible en cualquier máquina y en Docker; un archivo más en la raíz.

## R1-02 · Formato de logs de Business
- **Contexto:** la sección 7.2 exige logging estructurado con el TraceId de cada request.
- **Alternativas:** (a) consola JSON nativa (`AddJsonConsole` + scopes); (b) consola de texto con scopes; (c) Serilog.
- **Decisión:** (a). Cada línea es un JSON con `TraceId`, `SpanId` y los parámetros del mensaje.
- **Consecuencias:** sin dependencias extra y fácil de filtrar en `docker logs` (p. ej. por TraceId para seguir una llamada hasta Data); menos legible a simple vista que el texto plano.

## R1-03 · Interpretación de RN-10 (transiciones de estado)
- **Contexto:** RN-10 dice REPORTED → VERIFIED → ATTENDED sin retroceder; no aclara si se puede saltar un estado.
- **Alternativas:** (a) solo el paso siguiente (o quedarse igual); (b) cualquier avance.
- **Decisión:** (a) paso a paso.
- **Consecuencias:** trazabilidad completa del ciclo de atención; la Web debe ofrecer solo el estado siguiente.

## R1-04 · Validación de reglas de negocio
- **Alternativas:** (a) validadores escritos a mano; (b) FluentValidation; (c) data annotations.
- **Decisión:** (a). Las reglas de campo viven en `Domain/ImpactRules` y las reutilizan la API y la importación.
- **Consecuencias:** sin dependencias y mensajes en español controlados; un poco más de código.

## R1-05 · UI de OpenAPI
- **Decisión:** Scalar (`/scalar/v1`) sobre el documento OpenAPI nativo de .NET 10 (`/openapi/v1.json`), activo en Development o con `OpenApi:Enabled=true`.

## R1-06 · Estructura interna de Business
- **Decisión:** la sugerida en 7.2 (`Api/`, `Application/`, `Domain/`, `Validation/`, `Import/`, `DataGateway/`, `Mapping/`, `Errors/`, `GeoJson/`). La fachada `IDataGateway` trabaja con DTOs; solo `GrpcDataGateway` y `Mapping/ProtoMapping` conocen los mensajes del `.proto`.

## R1-07 · Detalles de implementación de las reglas (interpretaciones menores)
- **ALREADY_EXISTS en `CreateImpact`:** como el reintento automático de gRPC no informa si hubo reintento, Business hace `GetImpact` y compara el contenido (evento, coordenadas, personas, fecha). Si coincide es el mismo insert → 201; si no → 409.
- **RN-09:** si un POST trae `source = FILE`, se registra como MANUAL (FILE solo lo asigna la importación).
- **RN-06 / número de línea:** en CSV es la línea física (encabezado = 1, las líneas en blanco cuentan); en GeoJSON es el número de feature (base 1).
- **RN-06 / ninguna válida:** 400 con el `ImportReport` como cuerpo (no ProblemDetails), para que la Web pinte la misma tabla de errores.
- **RN-07:** la clave de duplicado usa la fecha ya resuelta; dos filas en el mismo punto y ambas sin `reported_at` se consideran duplicadas.
- **Data caído:** según cuánto tarde el SO en rechazar la conexión, el cliente gRPC termina en UNAVAILABLE o en DEADLINE_EXCEEDED; ambos → 503 con mensaje amigable, siempre dentro del deadline de 3 s.
