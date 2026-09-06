[CmdletBinding()]
param(
    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142',
    [switch]$SkipSync
)

# Dot-sourcing loads the shared connection, synchronization, and SSH helpers
# into this script's scope.
. (Join-Path $PSScriptRoot 'common.ps1')

# `$target` contains the checked SSH connection values. Missing user, host, and
# directory values use this website's defaults from common.ps1.
$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

# Certificate setup depends on the current server scripts and production .env,
# so the normal path synchronizes both first. SkipSync is intended only when the
# operator has already synchronized this exact checkout.
if (-not $SkipSync) {
    Update-RemoteDeploymentFiles -Target $target
}

# Verify the deployment library and project marker even when synchronization was
# skipped, preventing certificate setup in an empty or incorrect directory.
Confirm-RemoteDeploymentCheckout -Target $target

# Certificate installation uses sudo and Certbot may require terminal access,
# so the SSH connection receives an interactive TTY.
$ssh = Get-RequiredCommandPath ssh
$sshArguments = New-SshConnectionArguments -Target $target -Interactive

# `&` runs the executable stored in `$ssh`. The remote shell changes to the
# checked checkout before invoking its certificate setup entrypoint.
& $ssh @sshArguments $target.Address "cd '$($target.Directory)' && ./deployment/server/setup-certificate.sh"
if ($LASTEXITCODE -ne 0) {
    throw 'Let''s Encrypt certificate setup failed.'
}

Write-Host "Certificate setup completed for $($target.Address):$($target.Directory)"
