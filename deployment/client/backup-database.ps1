[CmdletBinding()]
param(
    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142',
    [string]$EnvFile,
    [string]$DestinationDirectory = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'GaifulinLab-backups'),
    [switch]$SkipSync
)

# Usage:
#   .\deployment\client\backup-database.ps1
# The backup is downloaded as a PostgreSQL custom-format .dump file. Use
# -SkipSync after this script has already been synchronized to the server.
. (Join-Path $PSScriptRoot 'common.ps1')

$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

if ($SkipSync -and -not [string]::IsNullOrWhiteSpace($EnvFile)) {
    throw 'EnvFile cannot be used together with SkipSync.'
}

if (-not $SkipSync) {
    Update-RemoteDeploymentFiles -Target $target -EnvFile $EnvFile
}

Confirm-RemoteDeploymentCheckout -Target $target

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    throw 'DestinationDirectory cannot be empty.'
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$resolvedDestinationDirectory = (Resolve-Path -LiteralPath $DestinationDirectory -ErrorAction Stop).Path

$ssh = Get-RequiredCommandPath ssh
$scp = Get-RequiredCommandPath scp
$interactiveSshArguments = New-SshConnectionArguments -Target $target -Interactive
$transferSshArguments = New-SshConnectionArguments -Target $target
$backupId = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([Guid]::NewGuid().ToString('N'))"
$remoteBackupFile = "/tmp/gaifulinlab-db-backup-$backupId.dump"
$localBackupFile = Join-Path $resolvedDestinationDirectory "gaifulinlab-$backupId.dump"

try {
    # A TTY is supplied because this command may request the operator's sudo
    # password on the server before pg_dump runs as the postgres system user.
    & $ssh @interactiveSshArguments $target.Address "cd '$($target.Directory)' && ./deployment/server/backup-database.sh '$remoteBackupFile'"
    if ($LASTEXITCODE -ne 0) {
        throw 'Remote PostgreSQL backup failed.'
    }

    & $scp @transferSshArguments "$($target.Address):$remoteBackupFile" $localBackupFile
    if ($LASTEXITCODE -ne 0) {
        throw 'Backup download failed.'
    }

    $backup = Get-Item -LiteralPath $localBackupFile -ErrorAction Stop
    if ($backup.Length -eq 0) {
        throw 'Downloaded backup is empty.'
    }

    $sizeMiB = [Math]::Round($backup.Length / 1MB, 2)
    Write-Host "Database backup saved to $($backup.FullName) ($sizeMiB MiB)."
}
catch {
    if (Test-Path -LiteralPath $localBackupFile -PathType Leaf) {
        Remove-Item -LiteralPath $localBackupFile -Force
    }

    throw
}
finally {
    # The remote file contains production data. Delete it after both successful
    # and failed transfers; a new GUID makes this cleanup safe to retry.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $ssh @transferSshArguments $target.Address "rm -f -- '$remoteBackupFile'" 1>$null 2>$null
    }
    catch {
        # Keep the original backup or transfer error as the useful diagnostic.
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}
