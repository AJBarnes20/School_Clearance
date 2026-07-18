#!/usr/bin/env bash
set -Eeuo pipefail

APP_IMAGE="schoolclearance-app"
LATEST_IMAGE="${APP_IMAGE}:latest"
PREVIOUS_IMAGE="${APP_IMAGE}:previous"
ROLLBACK_AVAILABLE=false
ROLLING_BACK=false

info() { printf '[INFO] %s\n' "$*"; }
ok() { printf '[ OK ] %s\n' "$*"; }
warn() { printf '[WARN] %s\n' "$*"; }
error() { printf '[ERROR] %s\n' "$*" >&2; }

require_command() {
    command -v "$1" >/dev/null 2>&1 || { error "Required command not found: $1"; exit 1; }
}

wait_healthy() {
    local container="$1"
    local attempts="${2:-60}"
    local status
    for ((i=1; i<=attempts; i++)); do
        status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container" 2>/dev/null || true)
        [ "$status" = "healthy" ] && return 0
        [ "$status" = "unhealthy" ] && return 1
        sleep 2
    done
    error "Timed out waiting for ${container}."
    return 1
}

rollback_image() {
    [ "$ROLLBACK_AVAILABLE" = true ] || return 1
    docker image inspect "$PREVIOUS_IMAGE" >/dev/null
    docker tag "$PREVIOUS_IMAGE" "$LATEST_IMAGE"
    docker compose up -d --remove-orphans --no-build
    wait_healthy schoolclearance-app
    wait_healthy schoolclearance-nginx
}

on_error() {
    error "Deployment failed."
    docker compose logs --tail=100 || true
    if [ "$ROLLBACK_AVAILABLE" = true ] && [ "$ROLLING_BACK" = false ]; then
        warn "Attempting automatic application-image rollback..."
        ROLLING_BACK=true
        trap - ERR
        rollback_image && ok "Previous application image restored." || error "Automatic rollback failed; run ./rollback.sh manually."
    fi
}
trap on_error ERR

require_command docker
docker compose version >/dev/null
require_command curl

if [ ! -f .env ]; then
    cp .env.example .env
    warn ".env created from .env.example. Fill in production values, then run ./deploy.sh again."
    exit 1
fi

set -a
# shellcheck disable=SC1091
source .env
set +a

: "${APP_PORT:=8086}"
: "${DB_NAME:=schoolclearance_db}"
: "${DB_USER:=schoolclearance}"
: "${DB_PASSWORD:?Set DB_PASSWORD in .env}"
: "${DB_ROOT_PASSWORD:?Set DB_ROOT_PASSWORD in .env}"
: "${APP_BASE_URL:?Set APP_BASE_URL in .env}"

info "Validating Docker Compose configuration..."
docker compose config --quiet

if docker image inspect "$LATEST_IMAGE" >/dev/null 2>&1; then
    docker tag "$LATEST_IMAGE" "$PREVIOUS_IMAGE"
    ROLLBACK_AVAILABLE=true
    ok "Saved current application image as ${PREVIOUS_IMAGE}."
else
    warn "First deployment: no previous image is available for rollback."
fi

info "Building application image..."
docker compose build schoolclearance-app

info "Starting containers..."
docker compose up -d --remove-orphans --no-build

info "Waiting for MySQL..."
wait_healthy schoolclearance-mysql 90
info "Waiting for the application..."
wait_healthy schoolclearance-app 60
info "Waiting for Nginx..."
wait_healthy schoolclearance-nginx 60

docker exec schoolclearance-nginx nginx -t >/dev/null
curl --fail --silent --show-error "http://localhost:${APP_PORT}/health" >/dev/null

docker image prune -f >/dev/null
ok "Deployment completed: ${APP_BASE_URL}"
docker compose ps
