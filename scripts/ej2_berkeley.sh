#!/usr/bin/env bash
# Exercise 2 - Berkeley. Output: docs/salidas/ej2_berkeley.txt
#   1-3. initial differences of 5, 10 and 20 seconds
#   4.   a node 5 minutes off with the plain average
#   5.   the same node with the fault-tolerant average
#   6.   a participant disconnects and comes back
#   7.   the coordinator stops: the clocks drift apart again
source "$(dirname "$0")/common.sh"
OUT=docs/salidas/ej2_berkeley.txt
: > "$OUT"

reset_env() {
  unset NODE1_OFFSET NODE2_OFFSET NODE3_OFFSET NODE2_DRIFT_PPM BERKELEY_ROUNDS BERKELEY_INTERVAL OUTLIER_THRESHOLD
  docker compose --profile berkeley down --remove-orphans >/dev/null 2>&1
}

logs() {
  docker compose logs --no-log-prefix coordinador
  echo
  for node in nodo-1 nodo-2 nodo-3; do docker compose logs --no-log-prefix "$node"; done
}

# scenario <title> <n1> <n2> <n3> [rounds] [threshold]
scenario() {
  reset_env
  export NODE1_OFFSET=$2 NODE2_OFFSET=$3 NODE3_OFFSET=$4 BERKELEY_ROUNDS=${5:-2} OUTLIER_THRESHOLD=${6:-0}
  scenario_start 3
  docker compose --profile berkeley up -d >/dev/null 2>&1
  wait_exit coordinador
  { section "$1"; echo "Relojes iniciales: coordinador 0 s, nodo-1 $2 s, nodo-2 $3 s, nodo-3 $4 s"; echo; logs; } >> "$OUT"
}

# Reads the three node clocks from the host and prints them relative to nodo-1.
snapshot() {
  python - "$1" <<'EOF'
import json, re, sys, urllib.request
from datetime import datetime
times = {}
for i in (1, 2, 3):
    with urllib.request.urlopen(f"http://localhost:1808{i}/api/clock", timeout=3) as r:
        text = re.sub(r"(\.\d{6})\d+", r"\1", json.load(r)["time"]).replace("Z", "+00:00")
        times[f"nodo-{i}"] = datetime.fromisoformat(text)
base = times["nodo-1"]
parts = [f"{n} {t:%H:%M:%S.%f}"[:-3] + f" ({(t - base).total_seconds() * 1000:+.0f} ms)" for n, t in times.items()]
spread = (max(times.values()) - min(times.values())).total_seconds() * 1000
print(f"{sys.argv[1]:<34} " + " | ".join(parts) + f" | dispersion {spread:.0f} ms")
EOF
}

scenario "Escenario 1 - diferencias iniciales de hasta 5 s" 5 -3 2
scenario "Escenario 2 - diferencias iniciales de hasta 10 s" 10 -6 4
scenario "Escenario 3 - diferencias iniciales de hasta 20 s" 20 -12 8
scenario "Escenario 4 - nodo-3 desfasado 5 minutos, promedio simple" 5 -3 300
scenario "Escenario 5 - nodo-3 desfasado 5 minutos, promedio tolerante a fallas (umbral 30 s)" 5 -3 300 2 30

# Scenario 6: nodo-3 disconnects after round 1 and comes back before round 4.
reset_env
export NODE1_OFFSET=10 NODE2_OFFSET=-6 NODE3_OFFSET=4 BERKELEY_ROUNDS=4
scenario_start 3
docker compose --profile berkeley up -d >/dev/null 2>&1
wait_log coordinador "Ronda 1 =" 60 && sleep 1
docker compose stop nodo-3 >/dev/null 2>&1
wait_log coordinador "Ronda 3 =" 60 && sleep 1
docker compose start nodo-3 >/dev/null 2>&1
wait_exit coordinador
{
  section "Escenario 6 - nodo-3 se desconecta despues de la ronda 1 y vuelve antes de la ronda 4"
  echo "Relojes iniciales: coordinador 0 s, nodo-1 10 s, nodo-2 -6 s, nodo-3 4 s"
  echo "nodo-3 detenido tras la ronda 1 (docker compose stop) y reiniciado tras la ronda 3 (vuelve con su reloj original)"
  echo
  logs
} >> "$OUT"

# Scenario 7: the coordinator fails; nodo-2 drifts 20 ms per second.
reset_env
export NODE1_OFFSET=10 NODE2_OFFSET=-6 NODE3_OFFSET=4 NODE2_DRIFT_PPM=20000 BERKELEY_ROUNDS=0
scenario_start 3
docker compose --profile berkeley up -d >/dev/null 2>&1
wait_log coordinador "Ronda 3 =" 60 && sleep 1
{
  section "Escenario 7 - falla el coordinador (nodo-2 deriva 20000 ppm = 20 ms/s)"
  echo "Relojes iniciales: coordinador 0 s, nodo-1 10 s, nodo-2 -6 s, nodo-3 4 s"
  echo
  snapshot "Con coordinador (tras la ronda 3)"
  docker compose stop coordinador >/dev/null 2>&1
  echo "--- se detiene el coordinador ---"
  for t in 5 10 20; do
    sleep 5; [[ $t == 20 ]] && sleep 5
    snapshot "Sin coordinador, +$t s"
  done
  echo
  logs
} >> "$OUT"
reset_env
echo "Salida en $OUT"
