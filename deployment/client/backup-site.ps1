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
#   .\deployment\client\backup-site.ps1
# Downloads one database dump and one media archive. Keep both files with the
# same timestamped id: an article's Markdown references media rows in the dump.
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
$remoteDatabaseFile = "/tmp/gaifulinlab-db-backup-$backupId.dump"
$remoteMediaFile = "/tmp/gaifulinlab-media-backup-$backupId.tar.gz"
$localDatabaseFile = Join-Path $resolvedDestinationDirectory "gaifulinlab-$backupId.dump"
$localMediaFile = Join-Path $resolvedDestinationDirectory "gaifulinlab-$backupId.media.tar.gz"

try {
    # pg_dump may request the operator's sudo password, so this SSH call needs a TTY.
    & $ssh @interactiveSshArguments $target.Address "cd '$($target.Directory)' && ./deployment/server/backup-database.sh '$remoteDatabaseFile' && ./deployment/server/backup-media.sh '$remoteMediaFile'"
    if ($LASTEXITCODE -ne 0) {
        throw 'Server backup creation failed.'
    }

    & $scp @transferSshArguments "$($target.Address):$remoteDatabaseFile" $localDatabaseFile
    if ($LASTEXITCODE -ne 0) {
        throw 'Database backup download failed.'
    }

    & $scp @transferSshArguments "$($target.Address):$remoteMediaFile" $localMediaFile
    if ($LASTEXITCODE -ne 0) {
        throw 'Media backup download failed.'
    }

    foreach ($backupFile in @($localDatabaseFile, $localMediaFile)) {
        if (-not (Test-Path -LiteralPath $backupFile -PathType Leaf) -or (Get-Item -LiteralPath $backupFile).Length -eq 0) {
            throw "Downloaded backup is missing or empty: $backupFile"
        }
    }

    Write-Host "Site backup saved to $resolvedDestinationDirectory (id: $backupId)."
}
catch {
    foreach ($backupFile in @($localDatabaseFile, $localMediaFile)) {
        if (Test-Path -LiteralPath $backupFile -PathType Leaf) {
            Remove-Item -LiteralPath $backupFile -Force
        }
    }

    throw
}
finally {
    # Both temporary files hold production content and are deleted after the transfer.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $ssh @transferSshArguments $target.Address "rm -f -- '$remoteDatabaseFile' '$remoteMediaFile'" 1>$null 2>$null
    }
    catch {
        # Keep a backup creation or transfer error as the useful diagnostic.
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}
