$ErrorActionPreference = 'Stop'

docker run --name gaifulinlab-postgres-dev `
    --restart unless-stopped `
    -e POSTGRES_DB=gaifulinlab `
    -e POSTGRES_USER=gaifulinlab `
    -e POSTGRES_PASSWORD=gaifulinlab `
    -p '127.0.0.1:5432:5432' `
    -v 'gaifulinlab-postgres-dev-data:/var/lib/postgresql/data' `
    -d postgres:17-alpine

if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the local PostgreSQL container.'
}
