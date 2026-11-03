# Datos de ejemplo

**Datos de ejemplo, NO oficiales.** Coordenadas aproximadas alrededor de Mocoa (Putumayo, avalancha 2017) y La Mojana (Sucre, inundación 2021), inventadas para pruebas y demo.

| Archivo | Filas | Esperado al importar |
|---|---|---|
| `impacts-sample.csv` | 20 | 17 aceptadas / 3 rechazadas: línea 5 (latitud 95), línea 10 (coordenada 0,0), línea 16 (`affected_people` negativo) |
| `impacts-sample.geojson` | 10 features | 10 aceptadas. Recuerde: `coordinates = [longitud, latitud]` |

Los números de línea cuentan el encabezado como línea 1.
