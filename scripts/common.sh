#!/usr/bin/env bash
# Helpers shared by the exercise scripts.
set -u
export MSYS_NO_PATHCONV=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
mkdir -p docs/salidas

# The real instant that every simulated clock shows as 10:00:00.
scenario_start() {
  export SCENARIO_START_UTC
  SCENARIO_START_UTC="$(date -u -d "+${1:-2} seconds" +%Y-%m-%dT%H:%M:%S.%3NZ)"
}

# wait_log <service> <text> [timeout seconds]
wait_log() {
  local limit=$(( $(date +%s) + ${3:-120} ))
  until docker compose logs --no-log-prefix "$1" 2>/dev/null | grep -q -- "$2"; do
    if (( $(date +%s) > limit )); then echo "TIMEOUT esperando '$2' en $1" >&2; return 1; fi
    sleep 0.5
  done
}

# wait_exit <service...>: waits until the containers of the services finish
wait_exit() {
  for service in "$@"; do
    docker wait "$(docker compose ps -a -q "$service")" >/dev/null
  done
}

section() { printf '\n===== %s =====\n\n' "$*"; }
