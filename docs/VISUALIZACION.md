# Visualizacion espacial - DisasterMaps

## Objetivo

Extender DisasterMaps para representar espacialmente la
severidad y el numero de personas afectadas por eventos
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

## Pendiente de validacion final

- Integracion end-to-end con PostgreSQL real.
- Rendimiento de mallas grandes.
- Reproduccion temporal automatica.
- Calibracion estadistica del variograma.