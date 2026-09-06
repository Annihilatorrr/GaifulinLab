[CmdletBinding()]
param(
    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142',
    [string]$EnvFile,
    [switch]$SkipSync
)

# Dot-sourcing loads the shared connection, synchronization, and SSH helpers
# into this script's scope.
. (Join-Path $PSScriptRoot 'common.ps1')

# `$target` contains the checked SSH connection values. Missing user, host, and
# directory values use this website's defaults from common.ps1.
$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

# EnvFile is consumed only by synchronization. Accepting it with SkipSync would
# imply that the file is uploaded even though all synchronization was disabled.
if ($SkipSync -and -not [string]::IsNullOrWhiteSpace($EnvFile)) {
    throw 'EnvFile cannot be used together with SkipSync.'
}

# A normal deployment first replaces the remote checkout and installs .env.
# SkipSync is intended for an explicitly pre-synchronized checkout.
if (-not $SkipSync) {
    Update-RemoteDeploymentFiles -Target $target -EnvFile $EnvFile
}

# Even with SkipSync, verify that the expected deployment library and project
# marker exist before invoking a server-side script.
Confirm-RemoteDeploymentCheckout -Target $target

# The server deployment may invoke sudo, so SSH receives an interactive TTY.
# `$ssh` is the resolved path to ssh.exe/ssh, while `@sshArguments` passes each
# connection option as a separate native-command argument.
$ssh = Get-RequiredCommandPath ssh
$sshArguments = New-SshConnectionArguments -Target $target -Interactive

# `&` runs the executable stored in `$ssh`. The final string is evaluated by the
# remote shell: it enters the checked checkout and runs its server deploy script.
& $ssh @sshArguments $target.Address "cd '$($target.Directory)' && ./deployment/server/deploy.sh"
if ($LASTEXITCODE -ne 0) {
    throw 'Remote production deployment failed.'
}

Write-Host "Deploy completed for $($target.Address):$($target.Directory)"
