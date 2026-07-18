#!/usr/bin/env bash
set -Eeuo pipefail

SEED_ID="Dump20260619"

shopt -s nullglob
sql_files=(/seed/*.sql)

if [ ${#sql_files[@]} -eq 0 ]; then
    echo "No SQL seed files found in /seed; leaving the database empty."
    exit 0
fi

existing_tables=$(mysql --protocol=socket -N -s -uroot -p"${MYSQL_ROOT_PASSWORD}" \
    -e "SELECT COUNT(*) FROM information_schema.tables
        WHERE table_schema = '${MYSQL_DATABASE}'
          AND table_name NOT IN ('deployment_seed_history');")

if [ "$existing_tables" -gt 0 ]; then
    echo "Database ${MYSQL_DATABASE} already contains ${existing_tables} tables; skipping seed import to prevent duplicate data."
    exit 0
fi

mysql --protocol=socket -uroot -p"${MYSQL_ROOT_PASSWORD}" "${MYSQL_DATABASE}" <<'SQL'
CREATE TABLE IF NOT EXISTS deployment_seed_history (
    seed_id VARCHAR(100) NOT NULL PRIMARY KEY,
    applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
SQL

already_seeded=$(mysql --protocol=socket -N -s -uroot -p"${MYSQL_ROOT_PASSWORD}" "${MYSQL_DATABASE}" \
    -e "SELECT COUNT(*) FROM deployment_seed_history WHERE seed_id = '${SEED_ID}';")

if [ "$already_seeded" -gt 0 ]; then
    echo "Seed ${SEED_ID} was already applied; skipping duplicate import."
    exit 0
fi

echo "Importing ${#sql_files[@]} School Clearance seed files..."
{
    echo "SET FOREIGN_KEY_CHECKS=0;"
    for sql_file in "${sql_files[@]}"; do
        echo "-- Importing ${sql_file}"
        # Workbench exports repeat GTID_PURGED in every per-table file. It is
        # not needed for a logical seed and repeated values can abort import.
        sed '/SET @@GLOBAL.GTID_PURGED=/d' "$sql_file"
    done
    echo "SET FOREIGN_KEY_CHECKS=1;"
    echo "INSERT INTO deployment_seed_history (seed_id) VALUES ('${SEED_ID}');"
} | mysql --protocol=socket -uroot -p"${MYSQL_ROOT_PASSWORD}" "${MYSQL_DATABASE}"

echo "School Clearance database initialization complete."
