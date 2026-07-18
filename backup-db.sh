#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
cd "$SCRIPT_DIR"

[ -f .env ] || { echo "[ERROR] .env not found." >&2; exit 1; }
set -a
# shellcheck disable=SC1091
source .env
set +a

: "${DB_NAME:=schoolclearance_db}"
: "${DB_ROOT_PASSWORD:?Set DB_ROOT_PASSWORD in .env}"
: "${BACKUP_DIR:=${SCRIPT_DIR}/backups}"
: "${BACKUP_RETENTION_DAYS:=7}"

mkdir -p "$BACKUP_DIR"
timestamp=$(date +'%Y-%m-%d_%H-%M-%S')
temporary_dump=$(mktemp)
trap 'rm -f "$temporary_dump"' EXIT

docker inspect schoolclearance-mysql >/dev/null 2>&1 || {
    echo "[ERROR] schoolclearance-mysql is not running." >&2
    exit 1
}

docker exec schoolclearance-mysql \
    mysqldump --single-transaction --routines --triggers \
    -uroot -p"${DB_ROOT_PASSWORD}" "${DB_NAME}" > "$temporary_dump"

current_hash=$(sha256sum "$temporary_dump" | awk '{print $1}')
hash_file="${BACKUP_DIR}/.db_last_hash"

if [ ! -f "$hash_file" ] || [ "$current_hash" != "$(cat "$hash_file")" ]; then
    gzip -c "$temporary_dump" > "${BACKUP_DIR}/${DB_NAME}_${timestamp}.sql.gz"
    printf '%s\n' "$current_hash" > "$hash_file"
    echo "[ OK ] Database backup created."
else
    echo "[ OK ] Database unchanged; no duplicate backup created."
fi

find "$BACKUP_DIR" -type f -name '*.sql.gz' -mtime "+${BACKUP_RETENTION_DAYS}" -delete
echo "[ OK ] Backup retention cleanup completed."
