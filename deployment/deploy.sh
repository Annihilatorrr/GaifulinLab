#!/usr/bin/env sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repository_root"

if [ ! -f .env ]; then
    echo "Missing .env. Copy .env.example and fill in the secrets." >&2
    exit 1
fi

require_secret() {
    name=$1
    value=$(sed -n "s/^${name}=//p" .env | tail -n 1)
    case "$value" in
        ""|replace-*)
            echo "Set a real value for ${name} in .env before deployment." >&2
            exit 1
            ;;
    esac
}

require_secret POSTGRES_PASSWORD
require_secret ADMIN_PASSWORD_HASH
require_secret JWT_SIGNING_KEY

docker compose config --quiet
docker compose build --pull app
docker compose up --detach --remove-orphans
docker compose ps
