#!/usr/bin/env bash
set -Eeuo pipefail

shopt -s nullglob
sql_files=(/seed/*.sql)

if [ ${#sql_files[@]} -eq 0 ]; then
    echo "No SQL seed files found in /seed; leaving the database empty."
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
} | mysql --protocol=socket -uroot -p"${MYSQL_ROOT_PASSWORD}" "${MYSQL_DATABASE}"

echo "School Clearance database initialization complete."
