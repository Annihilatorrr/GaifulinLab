$ErrorActionPreference = 'Stop'

$containerName = 'gaifulinlab-postgres-dev'
$imageName = 'postgres:17-alpine'
$databaseName = 'gaifulinlab'
$databaseUser = 'gaifulinlab'
$databasePassword = 'gaifulinlab'
$hostAddress = '127.0.0.1'
$hostPort = '5435'
$containerPort = '5432'
$volumeName = 'gaifulinlab-postgres-dev-data'
$postgresDataPath = '/var/lib/postgresql/data'
$restartPolicy = 'unless-stopped'

docker run --name $containerName `
    --restart $restartPolicy `
    -e "POSTGRES_DB=$databaseName" `
    -e "POSTGRES_USER=$databaseUser" `
    -e "POSTGRES_PASSWORD=$databasePassword" `
    -p "${hostAddress}:${hostPort}:${containerPort}" `
    -v "${volumeName}:${postgresDataPath}" `
    -d $imageName

if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the local PostgreSQL container.'
}
