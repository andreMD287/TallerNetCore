#!/usr/bin/env bash
# Verificación end-to-end de Business y medición de las metas (sección 7 de docs/ARQUITECTURA.md).
# Requiere: bash, curl, awk. Business accesible (local: dotnet run, o compose en modo dev).
#
#   scripts/verify.sh [http://localhost:5002]
#   STOP_DATA=1 scripts/verify.sh     # además mide la degradación deteniendo el contenedor data (compose)
set -uo pipefail

BASE="${1:-http://localhost:5002}"
API="$BASE/api/v1"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
pass=0; fail=0
ok()   { echo "  OK   $*"; pass=$((pass+1)); }
bad()  { echo "  FALLA $*"; fail=$((fail+1)); }
json() { grep -o "\"$1\":[^,}]*" | head -1 | cut -d: -f2- | tr -d '"'; }

echo "== Business en $BASE"
code=$(curl -s -o "$TMP/h" -w "%{http_code}" "$BASE/health")
[ "$code" = 200 ] && ok "GET /health 200 $(cat "$TMP/h")" || bad "GET /health -> $code $(cat "$TMP/h")"

echo "== 1. Crear evento"
EV=$(curl -s -X POST "$API/events" -H 'Content-Type: application/json' \
  -d '{"name":"Verificacion '"$(date +%H%M%S)"'","type":"FLOOD","description":"Creado por scripts/verify.sh"}' | json id)
[ -n "$EV" ] && ok "evento $EV" || { bad "no se pudo crear el evento"; exit 1; }

echo "== 2. Recibir una afectación (POST)"
code=$(curl -s -o "$TMP/p" -w "%{http_code}" -X POST "$API/impacts" -H 'Content-Type: application/json' \
  -d '{"eventId":"'"$EV"'","latitude":1.1522,"longitude":-76.6466,"affectedPeople":120,"municipality":"Mocoa","department":"Putumayo"}')
sev=$(json severity < "$TMP/p")
[ "$code" = 201 ] && [ "$sev" = HIGH ] && ok "201, severidad derivada $sev" || bad "POST -> $code $(cat "$TMP/p")"
ID=$(json id < "$TMP/p")

echo "== 3. Validación (lat 95) -> 400"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/impacts" -H 'Content-Type: application/json' -d '{"eventId":"'"$EV"'","latitude":95,"longitude":1}')
[ "$code" = 400 ] && ok "400 ValidationProblemDetails" || bad "esperado 400, llegó $code"

echo "== 4. Evento inexistente -> 422"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/impacts" -H 'Content-Type: application/json' -d '{"eventId":"11111111-1111-1111-1111-111111111111","latitude":4,"longitude":-74}')
[ "$code" = 422 ] && ok "422" || bad "esperado 422, llegó $code"

echo "== 5. Transición de estado (RN-10) y borrado lógico (RN-08)"
code=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$API/impacts/$ID" -H 'Content-Type: application/json' \
  -d '{"eventId":"'"$EV"'","latitude":1.1522,"longitude":-76.6466,"affectedPeople":120,"status":"VERIFIED"}')
[ "$code" = 200 ] && ok "REPORTED -> VERIFIED 200" || bad "PUT -> $code"
code=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "$API/impacts/$ID")
code2=$(curl -s -o /dev/null -w "%{http_code}" "$API/impacts/$ID")
[ "$code" = 204 ] && [ "$code2" = 404 ] && ok "DELETE 204 y luego GET 404" || bad "DELETE $code / GET $code2"

echo "== 6. Importar impacts-sample.csv (esperado 17/3)"
SAMPLE="$(dirname "$0")/../contracts/samples/impacts-sample.csv"
curl -s -F "file=@$SAMPLE" -F "eventId=$EV" "$API/impacts/import" > "$TMP/i"
acc=$(json accepted < "$TMP/i"); rej=$(json rejected < "$TMP/i")
[ "$acc" = 17 ] && [ "$rej" = 3 ] && ok "17 aceptadas / 3 rechazadas" || bad "reporte: $(cat "$TMP/i")"

echo "== 7. Meta: importación de 1.000 filas válidas (< 3 s)"
awk 'BEGIN{srand(7); print "latitude,longitude,severity,affected_people,municipality,department,description,reported_at";
  for(i=0;i<1000;i++) printf "%.6f,%.6f,,%d,Mocoa,Putumayo,carga %d,2026-10-%02dT%02d:%02d:00Z\n", 1.0+rand()*0.3, -76.8+rand()*0.3, int(rand()*900), i, 1+int(i/600), int(i/60)%10, i%60}' > "$TMP/1000.csv"
t=$(curl -s -o "$TMP/i2" -w "%{time_total}" -F "file=@$TMP/1000.csv" -F "eventId=$EV" "$API/impacts/import")
acc=$(json accepted < "$TMP/i2")
awk -v t="$t" 'BEGIN{exit !(t<3)}' && [ "$acc" = 1000 ] && ok "1.000 filas en ${t}s" || bad "1.000 filas: accepted=$acc en ${t}s"

echo "== 8. Meta: mapa con ~2.000 afectaciones (GET /impacts/geojson < 1 s)"
curl -s -o /dev/null -F "file=@$TMP/1000.csv;filename=otra.csv" -F "eventId=$EV" "$API/impacts/import" # ids nuevos: segunda carga
sed -i 's/2026-10-/2026-09-/' "$TMP/1000.csv"
curl -s -o /dev/null -F "file=@$TMP/1000.csv" -F "eventId=$EV" "$API/impacts/import"
curl -s -o /dev/null "$API/impacts/geojson" # calentamiento
t=$(curl -s -o "$TMP/g" -w "%{time_total}" "$API/impacts/geojson")
n=$(grep -o '"Feature"' "$TMP/g" | wc -l)
awk -v t="$t" 'BEGIN{exit !(t<1)}' && ok "$n features en ${t}s" || bad "$n features en ${t}s"

echo "== 9. Resumen por municipio"
code=$(curl -s -o /dev/null -w "%{http_code}" "$API/stats/by-municipality?eventId=$EV")
[ "$code" = 200 ] && ok "200" || bad "$code"

if [ "${STOP_DATA:-0}" = 1 ]; then
  echo "== 10. Meta: degradación con Data detenido (503 en <= 3,5 s) y recuperación"
  docker compose stop data >/dev/null
  r=$(curl -s -o /dev/null -w "%{http_code} %{time_total}" "$API/events")
  set -- $r
  [ "$1" = 503 ] && awk -v t="$2" 'BEGIN{exit !(t<=3.5)}' && ok "503 en ${2}s" || bad "Data detenido -> $1 en ${2}s"
  docker compose start data >/dev/null
  for i in $(seq 1 30); do [ "$(curl -s -o /dev/null -w "%{http_code}" "$API/events")" = 200 ] && break; sleep 1; done
  [ "$i" -lt 30 ] && ok "recuperado en ~${i}s sin reiniciar Business" || bad "no se recuperó en 30 s"
fi

echo "== Resultado: $pass OK, $fail fallas"
[ "$fail" = 0 ]
