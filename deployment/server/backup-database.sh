#!/usr/bin/env bash

# Create one temporary, portable PostgreSQL backup for download by the client
# script. The dump is intentionally written to stdout by pg_dump and redirected
# by the deployment user so the file stays readable only by that user.
set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"
DB_NAME_ENV_KEY="GAIFULINLAB_DB_NAME"

. "$SCRIPT_DIR/common.sh"

if (( $# != 1 )); then
    deployment_fail "Usage: $0 /tmp/gaifulinlab-db-backup-<id>.dump"
fi

BACKUP_FILE="$1"
[[ "$BACKUP_FILE" =~ ^/tmp/gaifulinlab-db-backup-[A-Za-z0-9._-]+\.dump$ ]] \
    || deployment_fail 'Backup path must be a GaifulinLab .dump file in /tmp.'

[[ -f "$ENV_FILE" ]] || deployment_fail "Production environment file is missing: $ENV_FILE"
DB_NAME="$(deployment_require_env "$DB_NAME_ENV_KEY" "$ENV_FILE")"
[[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] \
    || deployment_fail "$DB_NAME_ENV_KEY may contain only letters, numbers, and underscores."

backup_completed=false

cleanup_incomplete_backup() {
    if [[ "$backup_completed" != true ]]; then
        rm -f -- "$BACKUP_FILE"
    fi
}

trap cleanup_incomplete_backup EXIT
umask 077

# PostgreSQL is a host service. Connecting as its system user uses local peer
# authentication and avoids exposing the database password in a process list.
sudo -u postgres pg_dump \
    --format=custom \
    --no-owner \
    --no-privileges \
    --dbname="$DB_NAME" > "$BACKUP_FILE"

[[ -s "$BACKUP_FILE" ]] || deployment_fail 'PostgreSQL produced an empty backup.'
pg_restore --list "$BACKUP_FILE" >/dev/null
chmod 600 "$BACKUP_FILE"

backup_completed=true
printf '%s\n' "$BACKUP_FILE"
