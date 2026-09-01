# Starts the only local Docker dependency: the PDF worker.
# API, Web, and PostgreSQL continue to run from Visual Studio as usual.
$composeFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'docker-compose.local.yml'

# Удаляет контейнеры старого полного local Compose, но не трогает БД Visual Studio.
docker compose -f $composeFile up --detach --build --remove-orphans
exit $LASTEXITCODE
