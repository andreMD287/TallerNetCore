# Visualizacion espacial - DisasterMaps

## Objetivo

Extender DisasterMaps para representar espacialmente la
el nivel de afectacion (1 a 5) capturado por la app movil,
la severidad y el numero de personas afectadas por eventos
de desastre, sin modificar los contratos gRPC ni Data.

## Arquitectura

Web MVC + Leaflet
  -> REST de Business
  -> VisualizationService
  -> IDataGateway
  -> Data gRPC + PostgreSQL

## Tecnicas

- IDW: ponderacion inversa de la distancia.
- NEAREST: vecino mas cercano.
- KRIGING: kriging ordinario con modelo de
  covarianza esferica y sistema lineal.
- Validacion leave-one-out con MAE y RMSE.

## Endpoints de Business

GET /api/v1/visualization/interpolation

Parametros:
eventId, method, metric, from, to,
radiusKm, cellKm, maxCells.

GET /api/v1/visualization/compare

Devuelve metricas por metodo y cantidad de
muestras evaluadas u omitidas.

## Endpoints Web

GET /visualizacion
GET /visualizacion/events
GET /visualizacion/data
GET /visualizacion/compare

El navegador solo se comunica con Web.

## Visualizacion

La interfaz permite seleccionar evento,
tecnica, variable, radio y rango temporal.

Muestra una superficie de celdas coloreadas
en Leaflet con leyenda de cuatro niveles.

Permite exportar las celdas calculadas como
GeoJSON FeatureCollection de poligonos.

## Reglas y limitaciones

- Kriging exige al menos cinco ubicaciones.
- Maximo 500 afectaciones por interpolacion.
- Comparacion limitada a 100 muestras.
- Hasta 40000 celdas por solicitud.
- No extrapolar fuera del radio de busqueda.
- Los datos Fake son sinteticos y temporales.
- Una sola muestra no permite comparar
  correctamente las tres tecnicas.
- La validacion de precision depende de los
  datos; no representa certeza absoluta.
- El semivariograma usa una aproximacion
  inicial, sin calibracion automatica.
- La exportacion GeoJSON contiene estimaciones,
  no observaciones originales.
- Los filtros temporales permiten seleccionar
  intervalos; no incluyen reproduccion animada.

## Verificacion previa

- Pruebas matematicas: 24 aprobadas.
- REST Business: cuatro escenarios comprobados.
- Web: cinco rutas HTTP comprobadas.
- Demostracion Fake: nueve muestras,
  tres interpoladores y comparacion RMSE/MAE.

## Metricas del mapa de calor

| `metric` | Valor interpolado | Clases |
|---|---|---|
| `level` (por defecto) | Nivel de afectacion 1..5 de cada reporte | 5: Minima, Leve, Moderada, Grave, Critica (verde a rojo) |
| `severity` | Severidad LOW=1 .. CRITICAL=4 | 4 |
| `people` | log10(1 + personas afectadas) | 4, con los umbrales de `Severity` |

`level` es la metrica principal porque es la que pide el enunciado: identificar
que regiones fueron mas afectadas segun el nivel 1..5 reportado en campo.
Los registros sin nivel explicito lo derivan de su severidad (LOW=1 .. CRITICAL=4).

La comparacion de tecnicas usa validacion cruzada dejando uno fuera. Con mas de
100 puntos se calcula sobre una muestra uniforme y determinista de 100, porque
el costo de Kriging crece muy rapido con el numero de puntos.

## Validacion de punta a punta

Con `docker compose up -d --build` (PostgreSQL, Data, Business y Web) y 300
reportes sincronizados desde la app movil en un solo lote (nivel 5 en el
epicentro y decreciente hacia la periferia):

| Tecnica | RMSE | MAE |
|---|---:|---:|
| Kriging ordinario | 0,415 | 0,290 |
| IDW | 0,433 | 0,308 |
| Vecino mas cercano | 0,548 | 0,300 |

Kriging obtuvo el menor error cuadratico y es la tecnica recomendada cuando hay
al menos 5 puntos; IDW queda muy cerca con un costo menor. El mapa de calor por
nivel estimo valores entre 1 y 4,72 sobre una malla de 50 x 50 celdas.

## Extensiones futuras

- Rendimiento de mallas grandes.
- Reproduccion temporal automatica.
- Calibracion estadistica del variograma.