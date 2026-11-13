#!/usr/bin/env bash
# Starts the stack from empty, adds 2,000 tickets, and runs the k6 load test: the same starting point every time, so two
# runs can be compared. Needs Docker. Usage:
#   scripts/load/run.sh                      # 10 requesters + 10 agents, 30 s
#   REQUESTERS=100 AGENTS=100 scripts/load/run.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

docker compose down -v >/dev/null 2>&1
docker compose up -d >/dev/null
until curl -sf -o /dev/null http://localhost:8080/api/abp/application-configuration; do sleep 2; done
docker compose exec -T db psql -q -U helpdesk -d Helpdesk < scripts/load/seed.sql

# MSYS_NO_PATHCONV stops Git Bash on Windows from rewriting the mount path; it does nothing elsewhere
MSYS_NO_PATHCONV=1 docker run --rm -i -v "$PWD/scripts/load:/scripts" grafana/k6 run \
  -e BASE_URL=http://host.docker.internal:8080 \
  -e REQUESTERS="${REQUESTERS:-10}" -e AGENTS="${AGENTS:-10}" -e DURATION="${DURATION:-30s}" /scripts/load.js
