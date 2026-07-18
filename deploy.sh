#!/usr/bin/env bash
set -Eeuo pipefail

# Match the SSG Finance deployment logger: colored labels only for interactive
# terminals, NO_COLOR support, and clean redirected/CI output.
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ] && [ "${TERM:-}" != "dumb" ]; then
    COLOR_INFO=$'\033[36m'
    COLOR_OK=$'\033[32m'
    COLOR_WARN=$'\033[33m'
    COLOR_ERROR=$'\033[31m'
    COLOR_RESET=$'\033[0m'
else
    COLOR_INFO=''
    COLOR_OK=''
    COLOR_WARN=''
    COLOR_ERROR=''
    COLOR_RESET=''
fi

log_info() { printf '%b[INFO]%b %s\n' "$COLOR_INFO" "$COLOR_RESET" "$*"; }
log_ok() { printf '%b[ OK ]%b %s\n' "$COLOR_OK" "$COLOR_RESET" "$*"; }
log_warn() { printf '%b[WARN]%b %s\n' "$COLOR_WARN" "$COLOR_RESET" "$*"; }
log_error() { printf '%b[ERROR]%b %s\n' "$COLOR_ERROR" "$COLOR_RESET" "$*"; }

APP_IMAGE="schoolclearance-app"
LATEST_IMAGE="${APP_IMAGE}:latest"
PREVIOUS_IMAGE="${APP_IMAGE}:previous"
ROLLBACK_AVAILABLE=false
ROLLING_BACK=false

require_command() {
    command -v "$1" >/dev/null 2>&1 || { log_error "Required command not found: $1"; exit 1; }
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
    log_error "Timed out waiting for ${container}."
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
    echo
    log_error "Deployment failed."
    log_info "Showing recent container logs..."
    docker compose logs --tail=100 || true
    if [ "$ROLLBACK_AVAILABLE" = true ] && [ "$ROLLING_BACK" = false ]; then
        echo
        log_info "Attempting automatic application-image rollback..."
        ROLLING_BACK=true
        trap - ERR
        rollback_image && log_ok "Previous application image restored." || log_error "Automatic rollback failed; run ./rollback.sh manually."
    fi
}
trap on_error ERR

require_command docker
docker compose version >/dev/null
require_command curl

if [ ! -f .env ]; then
    cp .env.example .env
    log_ok ".env created from .env.example."
    log_warn "Fill in production values, then run ./deploy.sh again."
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

echo
echo "========================================"
echo "     School Clearance Deployment        "
echo "========================================"
echo

log_info "Checking database seed files..."
if [ ! -x docker/mysql/init-db.sh ]; then
    log_error "docker/mysql/init-db.sh is missing or not executable."
    exit 1
fi
shopt -s nullglob
SEED_FILES=(Dump20260619/*.sql)
shopt -u nullglob
if [ ${#SEED_FILES[@]} -eq 0 ]; then
    log_error "No SQL seed files found in Dump20260619/."
    exit 1
fi
log_ok "Database initializer and ${#SEED_FILES[@]} SQL seed files are ready."

log_info "Validating Docker Compose configuration..."
docker compose config --quiet
log_ok "Docker Compose configuration is valid."

if docker image inspect "$LATEST_IMAGE" >/dev/null 2>&1; then
    docker tag "$LATEST_IMAGE" "$PREVIOUS_IMAGE"
    ROLLBACK_AVAILABLE=true
    log_ok "Saved current application image as ${PREVIOUS_IMAGE}."
else
    log_warn "First deployment: no previous image is available for rollback."
fi

log_info "Building application image..."
docker compose build schoolclearance-app
log_ok "Application image built."

log_info "Starting containers..."
docker compose up -d --remove-orphans --no-build
log_ok "Containers started."

log_info "Waiting for MySQL..."
wait_healthy schoolclearance-mysql 90
log_ok "MySQL is healthy."
log_info "Waiting for the application..."
wait_healthy schoolclearance-app 60
log_ok "Application is healthy."
log_info "Waiting for Nginx..."
wait_healthy schoolclearance-nginx 60
log_ok "Nginx is healthy."

log_info "Validating Nginx configuration..."
docker exec schoolclearance-nginx nginx -t >/dev/null
log_ok "Nginx configuration is valid."

log_info "Checking application endpoint..."
curl --fail --silent --show-error "http://localhost:${APP_PORT}/health" >/dev/null
log_ok "Application is reachable."

log_info "Cleaning unused Docker images..."
docker image prune -f >/dev/null
log_ok "Cleanup complete."

echo
echo "========================================"
echo "    Deployment completed successfully   "
echo "========================================"
echo
echo "Application: ${APP_BASE_URL}"
if [ "$ROLLBACK_AVAILABLE" = true ]; then
    echo "Rollback:    ./rollback.sh  (restores ${PREVIOUS_IMAGE})"
fi
echo

docker compose ps
