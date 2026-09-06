[CmdletBinding()]
param(
    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142',
    [string]$EnvFile
)

# Dot-sourcing loads the shared deployment functions into this script's scope.
. (Join-Path $PSScriptRoot 'common.ps1')

# `$target` contains the checked SSH user, host, remote directory, identity file,
# and combined Address (`user@host`). Missing connection values use the defaults
# defined by this website's DeploymentConfiguration in common.ps1.
$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

# This builds a Git-based archive, checks its contents, updates the remote
# checkout, and installs the production environment file. When `EnvFile` is not
# supplied, the function uses this repository's deployment/.env.
Update-RemoteDeploymentFiles -Target $target -EnvFile $EnvFile

Write-Host "Sync completed for $($target.Address):$($target.Directory)"
