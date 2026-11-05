# DisasterMaps Campo (app móvil)

App Android para recolectar en campo la **latitud, la longitud y el nivel de afectación (1 a 5)** de los sitios de desastre. **Funciona sin conexión** y **sincroniza con el sistema central** cuando vuelve la red.

Diseño: `docs/ARQUITECTURA.md`, sección 10. Decisiones: `docs/DECISIONS.md`, de DEC-10 a DEC-17. Contrato: `contracts/rest/api-v1.md`, sección 6.

## Cómo funciona

- **Reportar:** se elige el evento, la ubicación y el nivel de 1 a 5. La ubicación sale del GPS (funciona sin internet), de un toque en el mapa o se escribe a mano. Las personas afectadas, el lugar y la descripción son opcionales.
- **Sin conexión:** todo se guarda primero en SQLite dentro del teléfono. La franja superior muestra siempre si hay conexión y cuántos reportes faltan por enviar.
- **Al volver la red:** la app envía los pendientes en lotes a `POST /api/v1/sync/batch`. También lo hace al abrirla, cada minuto mientras está abierta y con el botón "Enviar".
  - Si el envío falla, espera 30 s, 1, 2, 4… hasta 15 min antes de reintentar.
  - Un reporte solo se marca como "Sincronizado" cuando el servidor confirma que lo tiene.
- **Reenviar es seguro:** cada reporte tiene un id generado en el teléfono. Si se pierde la respuesta del servidor y la app reenvía, el servidor responde `DUPLICATE` y no se crea nada repetido.
- **Rechazados y conflictos** quedan en "Reportes › Revisar" con el motivo del servidor. Un rechazado se corrige y se reenvía. Un conflicto se envía como reporte nuevo o se descarta.

## Instalar y probar

1. Levante el sistema central desde la raíz del repo con `docker compose up -d --build`. Business queda en el puerto 5002.
2. Instale el APK (`build/app/outputs/flutter-apk/app-release.apk`).
3. En **Ajustes**, escriba la dirección del servidor y pulse "Guardar y probar conexión":
   - Emulador de Android: `http://10.0.2.2:5002`
   - Teléfono real en la misma red Wi-Fi que el computador: `http://<IP del computador>:5002`. Si no conecta, permita el puerto 5002 en el firewall de Windows.
4. Pulse **Descargar eventos**. Desde ahí se puede reportar sin conexión.
5. Active el modo avión, registre varios reportes y desactívelo. En menos de un minuto quedan sincronizados y aparecen en el mapa web (`http://localhost:5001`) con origen `MOBILE`.

## Desarrollo

```bash
flutter pub get
flutter analyze
flutter test                                   # pruebas con servidor simulado y SQLite en memoria
flutter test test/live_server_test.dart --dart-define=SERVER_URL=http://localhost:5002   # contra el sistema real
flutter build apk --release
```

| Carpeta | Contenido |
|---|---|
| `lib/core/` | Nivel de afectación (etiquetas y colores) y validación de coordenadas (RN-01) |
| `lib/data/` | Modelo del reporte, SQLite, repositorios y ajustes |
| `lib/sync/` | Cliente REST y motor de sincronización |
| `lib/state/` | Estado de la app: conectividad, sincronización automática |
| `lib/ui/` | Pantallas: Reportar, Reportes, Detalle, Mapa, Elegir en el mapa, Ajustes |
