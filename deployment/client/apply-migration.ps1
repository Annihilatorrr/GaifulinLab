[CmdletBinding()]
param(
    [string]$RemoteUser,
    [string]$RemoteHost,
    [string]$RemoteDir,
    [string]$IdentityFile = 'C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142',
    [switch]$SkipSync
)

# `Join-Path` combines this script's folder (`$PSScriptRoot`) with `common.ps1` without hard-coding separators.
# The leading dot (`.`) runs that file in this script's current scope, so functions such as
# `New-RemoteDeploymentConnection` becomes available below; it is PowerShell's equivalent of importing a small helper library.
. (Join-Path $PSScriptRoot 'common.ps1')

# `$target` is a PSCustomObject with the checked remote connection settings:
# User, Host, Directory, IdentityFile, and Address (`user@host`).
$target = New-RemoteDeploymentConnection -RemoteUser $RemoteUser -RemoteHost $RemoteHost -RemoteDir $RemoteDir -IdentityFile $IdentityFile

# `SkipSync` is a switch: it is `$true` only when the command was called with `-SkipSync`.
# `-not` reverses that value. Therefore the `{ ... }` block uploads the current checkout before migration
# unless the operator explicitly said that the remote checkout is already current.
if (-not $SkipSync) { Update-RemoteDeploymentFiles -Target $target }
Confirm-RemoteDeploymentCheckout -Target $target

# `Get-RequiredCommandPath ssh` locates the `ssh` executable and throws a clear error if it is unavailable.
# `$ssh` receives its path (for example, `C:\Windows\System32\OpenSSH\ssh.exe`) for the `& $ssh` call below.
$ssh = Get-RequiredCommandPath ssh

# `@sshArguments` passes every array item as a separate SSH option.
# For example, `@('-o', 'BatchMode=yes')` becomes `ssh -o BatchMode=yes <host> <command>`.
$sshArguments = New-SshConnectionArguments -Target $target

# `&` is PowerShell's call operator: it runs the `ssh` executable whose path is stored in `$ssh`.
& $ssh @sshArguments $target.Address "cd '$($target.Directory)' && ./deployment/server/migrate.sh"
if ($LASTEXITCODE) { throw 'Remote production migration failed.' }
Write-Host 'Production migrations completed.'
