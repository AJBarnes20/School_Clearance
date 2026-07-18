#!/usr/bin/env bash
set -Eeuo pipefail

LATEST_IMAGE="schoolclearance-app:latest"
PREVIOUS_IMAGE="schoolclearance-app:previous"

wait_healthy() {
    local container="$1"
    for _ in {1..60}; do
        status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container" 2>/dev/null || true)
        [ "$status" = "healthy" ] && return 0
        [ "$status" = "unhealthy" ] && return 1
        sleep 2
    done
    return 1
}

[ -f .env ] || { echo "[ERROR] .env not found." >&2; exit 1; }
set -a
# shellcheck disable=SC1091
source .env
set +a
: "${APP_PORT:=8086}"

docker image inspect "$PREVIOUS_IMAGE" >/dev/null 2>&1 || {
    echo "[ERROR] No previous image exists: ${PREVIOUS_IMAGE}" >&2
    exit 1
}

docker tag "$PREVIOUS_IMAGE" "$LATEST_IMAGE"
docker compose up -d --remove-orphans --no-build
wait_healthy schoolclearance-mysql
wait_healthy schoolclearance-app
wait_healthy schoolclearance-nginx
docker exec schoolclearance-nginx nginx -t >/dev/null
curl --fail --silent --show-error "http://localhost:${APP_PORT}/health" >/dev/null

echo "[ OK ] Restored ${PREVIOUS_IMAGE} as ${LATEST_IMAGE}."
docker compose ps
