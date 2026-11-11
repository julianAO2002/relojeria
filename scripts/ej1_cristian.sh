#!/usr/bin/env bash
# Exercise 1 - Cristian. Output: docs/salidas/ej1_cristian.txt
#  - servidor-A runs the network phases: normal, symmetric latency, asymmetric latency, very high RTT
#  - servidor-B (+8 s, drifting 5000 ppm) and servidor-C (-5 s) synchronize on a normal network
#  - in the middle of the periodic resynchronizations the time server is stopped and restarted
source "$(dirname "$0")/common.sh"
OUT=docs/salidas/ej1_cristian.txt

docker compose --profile cristian down --remove-orphans >/dev/null 2>&1
scenario_start 2
export CRISTIAN_ROUNDS=7
docker compose --profile cristian up -d >/dev/null 2>&1

wait_log client-a "Resincronizacion periodica 1/" 90
docker compose stop time-server >/dev/null 2>&1
STOPPED="$(date -u +%H:%M:%S)"
sleep 9
docker compose start time-server >/dev/null 2>&1
STARTED="$(date -u +%H:%M:%S)"

wait_exit client-a client-b client-c
{
  echo "Escenario: hora simulada 10:00:00 = ${SCENARIO_START_UTC} (UTC real)"
  echo "Servidor de tiempo detenido a las ${STOPPED} y reiniciado a las ${STARTED} (UTC real), ~9 s sin servicio"
  for service in time-server client-a client-b client-c; do
    section "$service"
    docker compose logs --no-log-prefix "$service"
  done
} > "$OUT"
docker compose --profile cristian down >/dev/null 2>&1
echo "Salida en $OUT"
