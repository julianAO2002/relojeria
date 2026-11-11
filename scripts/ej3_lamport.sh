#!/usr/bin/env bash
# Exercise 3 - Lamport. Output: docs/salidas/ej3_lamport.txt
# Physical clocks: OrdersService 0 s, InventoryService -3 s, PaymentsService +5 s.
# PaymentsService starts with 4 unrelated internal events (Lamport 4).
source "$(dirname "$0")/common.sh"
OUT=docs/salidas/ej3_lamport.txt
ORDERS=http://localhost:8090

docker compose --profile lamport down --remove-orphans >/dev/null 2>&1
scenario_start 3
docker compose --profile lamport up -d >/dev/null 2>&1
for _ in $(seq 1 60); do curl -sf "$ORDERS/health" >/dev/null && break; sleep 0.5; done
sleep 1

{
  echo "Escenario: hora simulada 10:00:00 = ${SCENARIO_START_UTC} (UTC real)"
  echo "Relojes fisicos: OrdersService +0 s | InventoryService -3 s | PaymentsService +5 s"

  section "Pedido 1: P001 x 2 (hay stock)"
  echo "\$ curl -X POST $ORDERS/api/orders -d '{\"productId\":\"P001\",\"quantity\":2,\"amount\":150}'"
  curl -s -w ' [HTTP %{http_code}]\n' -X POST "$ORDERS/api/orders" -H "content-type: application/json" \
    -d '{"productId":"P001","quantity":2,"amount":150}'

  section "Pedido 2: P003 x 1 (sin stock)"
  echo "\$ curl -X POST $ORDERS/api/orders -d '{\"productId\":\"P003\",\"quantity\":1,\"amount\":80}'"
  curl -s -w ' [HTTP %{http_code}]\n' -X POST "$ORDERS/api/orders" -H "content-type: application/json" \
    -d '{"productId":"P003","quantity":1,"amount":80}'

  section "Registro de cada servicio"
  for service in orders-service inventory-service payments-service; do
    docker compose logs --no-log-prefix "$service"
    echo
  done

  section "GET /api/timeline (eventos de los tres servicios)"
  curl -s "$ORDERS/api/timeline"
} > "$OUT"

docker compose --profile lamport down >/dev/null 2>&1
echo "Salida en $OUT"
