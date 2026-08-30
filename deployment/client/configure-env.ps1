[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvFile,

    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\rbpi0807'
)

# Dot-sourcing loads the shared connection and environment-file helpers into
# this script's scope.
. (Join-Path $PSScriptRoot 'common.ps1')

# `$target` contains the checked SSH connection values. Missing user, host, and
# directory values use this website's defaults from common.ps1.
$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

# This validates EnvFile as data, uploads it through a temporary remote file with
# mode 600, and atomically replaces deployment/.env. Source files and services
# are intentionally left unchanged by this configuration-only command.
Install-ProductionEnvironmentFile -Target $target -EnvFile $EnvFile

Write-Host "Production environment configured at $($target.Address):$($target.Directory)/deployment/.env"
