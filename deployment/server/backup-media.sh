#!/usr/bin/env bash

# Create a portable archive of the server-owned image directory. The archive is
# intentionally produced in /tmp so the client script can download and remove it.
set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"

. "$SCRIPT_DIR/common.sh"

if (( $# != 1 )); then
    deployment_fail "Usage: $0 /tmp/gaifulinlab-media-backup-<id>.tar.gz"
fi

BACKUP_FILE="$1"
[[ "$BACKUP_FILE" =~ ^/tmp/gaifulinlab-media-backup-[A-Za-z0-9._-]+\.tar\.gz$ ]] \
    || deployment_fail 'Backup path must be a GaifulinLab media .tar.gz file in /tmp.'

require_file "$ENV_FILE"
MEDIA_HOST_PATH="$(deployment_optional_env GAIFULINLAB_MEDIA_HOST_PATH "$DEPLOYMENT_DIR/runtime/media" "$ENV_FILE")"
[[ "$MEDIA_HOST_PATH" = /* && "$MEDIA_HOST_PATH" != / ]] \
    || deployment_fail 'GAIFULINLAB_MEDIA_HOST_PATH must be an absolute non-root path.'
[[ -d "$MEDIA_HOST_PATH" ]] \
    || deployment_fail "Media directory does not exist: $MEDIA_HOST_PATH"

backup_completed=false

cleanup_incomplete_backup() {
    if [[ "$backup_completed" != true ]]; then
        rm -f -- "$BACKUP_FILE"
    fi
}

trap cleanup_incomplete_backup EXIT
umask 077

# Store one top-level directory in the archive. It can be restored beside an
# empty runtime directory without relying on the original absolute server path.
tar -czf "$BACKUP_FILE" \
    -C "$(dirname -- "$MEDIA_HOST_PATH")" \
    -- "$(basename -- "$MEDIA_HOST_PATH")"

[[ -s "$BACKUP_FILE" ]] || deployment_fail 'Media archive is empty.'
tar -tzf "$BACKUP_FILE" >/dev/null
chmod 600 "$BACKUP_FILE"

backup_completed=true
printf '%s\n' "$BACKUP_FILE"
