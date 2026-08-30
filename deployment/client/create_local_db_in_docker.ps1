[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$containerName = 'gaifulinlab-postgres-dev'
$databaseName = 'gaifulinlab'
$databaseUser = 'gaifulinlab'
$databasePassword = 'gaifulinlab'
$volumeName = 'gaifulinlab-postgres-dev-data'
$imageName = 'postgres:17-alpine'

$docker = Get-Command -Name docker -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $docker) {
    throw 'Docker was not found in PATH.'
}

& $docker.Path info *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker daemon is unavailable.'
}

& $docker.Path container inspect $containerName *> $null
$containerExists = $LASTEXITCODE -eq 0

if ($containerExists) {
    $containerIsRunning = (& $docker.Path inspect --format '{{.State.Running}}' $containerName) -eq 'true'

    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect Docker container '$containerName'."
    }

    if ($containerIsRunning) {
        Write-Host "Container '$containerName' is already running."
    }
    else {
        & $docker.Path start $containerName

        if ($LASTEXITCODE -ne 0) {
            throw "Could not start Docker container '$containerName'."
        }
    }
}
else {
    & $docker.Path run `
        --name $containerName `
        --restart unless-stopped `
        --env "POSTGRES_DB=$databaseName" `
        --env "POSTGRES_USER=$databaseUser" `
        --env "POSTGRES_PASSWORD=$databasePassword" `
        --publish '127.0.0.1:5432:5432' `
        --volume "${volumeName}:/var/lib/postgresql/data" `
        --detach `
        $imageName

    if ($LASTEXITCODE -ne 0) {
        throw "Could not create Docker container '$containerName'."
    }
}

for ($attempt = 1; $attempt -le 30; $attempt++) {
    & $docker.Path exec $containerName `
        pg_isready `
        --username $databaseUser `
        --dbname $databaseName *> $null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "PostgreSQL is ready at localhost:5432/$databaseName."
        exit 0
    }

    Start-Sleep -Seconds 1
}

& $docker.Path logs --tail 100 $containerName
throw "PostgreSQL container '$containerName' did not become ready."
