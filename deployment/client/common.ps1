$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This file is the shared client-side deployment library. The implementation is
# intentionally identical in both website repositories; only this configuration
# object contains details that belong to a particular website.
$script:DeploymentDirectory = Split-Path -Parent $PSScriptRoot
$script:DeploymentRepositoryRoot = Split-Path -Parent $script:DeploymentDirectory
$script:DeploymentConfiguration = [PSCustomObject]@{
    Name = 'GaifulinLab'
    DefaultRemoteUser = 'ruslan'
    DefaultRemoteHost = '192.168.50.142'
    RemoteDirectoryName = 'gaifulinlab'
    RepositoryMarkerPath = 'src/GaifulinLab.Api/GaifulinLab.Api.csproj'
    RequiredProductionEnvironmentKeys = @(
        'GAIFULINLAB_PUBLIC_ORIGIN',
        'GAIFULINLAB_DB_HOST',
        'GAIFULINLAB_DB_PORT',
        'GAIFULINLAB_DB_NAME',
        'GAIFULINLAB_DB_USER',
        'GAIFULINLAB_DB_PASSWORD',
        'GAIFULINLAB_JWT_SIGNING_KEY',
        'GAIFULINLAB_TLS_EMAIL',
        'GAIFULINLAB_HOST_NGINX_SERVER_NAMES',
        'GAIFULINLAB_HOST_NGINX_CERT_FILE',
        'GAIFULINLAB_HOST_NGINX_CERT_KEY'
    )
}

# Finds a native executable such as ssh, scp, tar, or git. A deployment should
# fail before any remote work when a prerequisite is missing, rather than
# continuing with a partial local setup.
function Get-RequiredCommandPath {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $command = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        throw "The required command '$Name' was not found in PATH."
    }

    return $command.Path
}

# Builds one checked connection object for all remote operations. The object
# holds User, Host, Directory, IdentityFile, and Address (the user@host value
# accepted by ssh and scp), so callers do not need to rebuild these values.
function New-RemoteDeploymentConnection {
    param(
        [string]$RemoteUser,
        [string]$RemoteHost,
        [string]$RemoteDir,
        [string]$IdentityFile
    )

    $configuration = $script:DeploymentConfiguration

    # Entry scripts normally pass explicit connection settings. These defaults
    # keep direct calls to this helper usable without reading process environment
    # variables whose presence can be difficult for an operator to discover.
    if ([string]::IsNullOrWhiteSpace($RemoteUser)) {
        $RemoteUser = $configuration.DefaultRemoteUser
    }

    if ([string]::IsNullOrWhiteSpace($RemoteHost)) {
        $RemoteHost = $configuration.DefaultRemoteHost
    }

    if ([string]::IsNullOrWhiteSpace($RemoteDir)) {
        $RemoteDir = "/home/$RemoteUser/deployments/$($configuration.RemoteDirectoryName)"
    }

    # Values are interpolated into small remote shell commands below. Restricting
    # them here prevents shell metacharacters from becoming part of those commands.
    if ($RemoteUser -notmatch '^[A-Za-z0-9._-]+$') {
        throw 'RemoteUser contains unsupported characters.'
    }

    if ($RemoteHost -notmatch '^[A-Za-z0-9.:-]+$') {
        throw 'RemoteHost contains unsupported characters.'
    }

    $expectedRemoteDirectory = "/home/$RemoteUser/deployments/$($configuration.RemoteDirectoryName)"
    if ($RemoteDir -ne $expectedRemoteDirectory) {
        throw "RemoteDir must be '$expectedRemoteDirectory'."
    }

    if ($IdentityFile -and -not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
        throw "SSH identity file not found: $IdentityFile"
    }

    return [PSCustomObject]@{
        User = $RemoteUser
        Host = $RemoteHost
        Directory = $RemoteDir
        IdentityFile = $IdentityFile
        Address = "$RemoteUser@$RemoteHost"
    }
}

# Creates the transport options shared by every ssh and scp invocation.
# Non-interactive operations use BatchMode so a missing credential fails instead
# of waiting for input; operations that may call sudo request a terminal with -tt.
function New-SshConnectionArguments {
    param(
        [Parameter(Mandatory)]
        $Target,

        [switch]$Interactive
    )

    [string[]]$arguments = if ($Interactive) {
        @('-tt')
    }
    else {
        @('-o', 'BatchMode=yes')
    }

    if ($Target.IdentityFile) {

        # IdentitiesOnly prevents ssh from trying unrelated agent keys before the
        # explicitly selected identity file.
        $arguments += @('-i', $Target.IdentityFile, '-o', 'IdentitiesOnly=yes')
    }

    return $arguments
}

# Sends the current source checkout and production environment file to the
# server. It deliberately rebuilds the remote checkout instead of uploading
# changed files one by one, which avoids stale source files after renames.
function Update-RemoteDeploymentFiles {
    param(
        [Parameter(Mandatory)]
        $Target,

        [string]$EnvFile
    )

    $configuration = $script:DeploymentConfiguration
    if ([string]::IsNullOrWhiteSpace($EnvFile)) {
        $EnvFile = Join-Path $script:DeploymentDirectory '.env'
    }

    Confirm-ProductionEnvironmentFile -Path $EnvFile

    $ssh = Get-RequiredCommandPath ssh
    $scp = Get-RequiredCommandPath scp
    $tar = Get-RequiredCommandPath tar
    $git = Get-RequiredCommandPath git
    $sshArguments = New-SshConnectionArguments -Target $Target
    $transferId = "$(Get-Date -Format 'yyyyMMddHHmmss')-$([Guid]::NewGuid().ToString('N'))"
    $archive = Join-Path ([IO.Path]::GetTempPath()) "$($configuration.RemoteDirectoryName)-sync-$transferId.tar.gz"
    $manifest = Join-Path ([IO.Path]::GetTempPath()) "$($configuration.RemoteDirectoryName)-files-$transferId.txt"
    $remoteArchive = "/tmp/$($configuration.RemoteDirectoryName)-sync-$transferId.tar.gz"

    try {

        # Git supplies a portable manifest containing tracked files plus useful
        # untracked files, while respecting .gitignore. This keeps build output,
        # IDE state, and other local-only files out of the deployment archive.
        # Keep non-ASCII names as real Unicode text. Otherwise Git returns
        # C-style quoted paths that Windows cannot pass to Test-Path.
        $relativeFiles = @(& $git -C $script:DeploymentRepositoryRoot -c core.quotePath=false ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not enumerate repository files with git.'
        }

        $relativeFiles = @(
            $relativeFiles | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                # Private-use characters are supported by NTFS but not by
                # Windows tar. Such paths are local build artifacts.
                $_ -notmatch '\p{Co}' -and
                (Test-Path -LiteralPath (Join-Path $script:DeploymentRepositoryRoot $_) -PathType Leaf)
            }
        )

        if ($relativeFiles.Count -eq 0) {
            throw 'The deployment file manifest is empty.'
        }

        [IO.File]::WriteAllLines($manifest, [string[]]$relativeFiles, [Text.UTF8Encoding]::new($false))
        & $tar -czf $archive -C $script:DeploymentRepositoryRoot -T $manifest
        if ($LASTEXITCODE -ne 0) {
            throw 'Deployment archive creation failed.'
        }

        # Inspect the finished archive as a second boundary check. A future
        # .gitignore or tar change must not accidentally ship private state,
        # build artifacts, local installers, or files from .git itself.
        $archiveEntries = @(& $tar -tzf $archive)
        if ($LASTEXITCODE -ne 0) {
            throw 'Deployment archive inspection failed.'
        }

        $forbiddenEntries = @(
            $archiveEntries | Where-Object {
                $_ -match '(^|/)(\.git|bin|obj|installers|runtime|generated|packages|certs)(/|$)' -or
                (
                    $_ -match '(^|/)\.env($|\.)' -and
                    $_ -ne 'deployment/.env' -and
                    $_ -notmatch '(^|/)\.env(\.local)?\.example$'
                )
            }
        )

        if ($forbiddenEntries.Count -gt 0) {
            throw "Deployment archive contains forbidden files: $($forbiddenEntries -join ', ')"
        }

        $sizeMiB = [Math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 2)
        Write-Host "Prepared deployment archive: $sizeMiB MiB."

        # Create only the deployment directory before transfer. The cleanup is
        # performed later, after the complete archive is safely on the server.
        & $ssh @sshArguments $Target.Address "mkdir -p '$($Target.Directory)/deployment'"
        if ($LASTEXITCODE -ne 0) {
            throw 'Remote deployment directory creation failed.'
        }

        & $scp @sshArguments $archive "$($Target.Address):$remoteArchive"
        if ($LASTEXITCODE -ne 0) {
            throw 'Deployment archive upload failed.'
        }

        # Preserve server-owned configuration, certificates, generated assets,
        # packages, and runtime data. All remaining checkout files are replaced
        # atomically enough for this single-user deployment workflow.
        $remoteExtractionCommand = "set -eu; root='$($Target.Directory)'; archive='$remoteArchive'; find `"`$root`" -mindepth 1 -maxdepth 1 ! -name deployment -exec rm -rf {} +; find `"`$root/deployment`" -mindepth 1 -maxdepth 1 ! -name .env ! -name umami.env ! -name runtime ! -name generated ! -name packages ! -name certs -exec rm -rf {} +; tar -xzf `"`$archive`" -C `"`$root`"; rm -f `"`$archive`"; chmod +x `"`$root`"/deployment/server/*.sh"
        & $ssh @sshArguments $Target.Address $remoteExtractionCommand
        if ($LASTEXITCODE -ne 0) {
            throw 'Remote archive extraction failed.'
        }

        Install-ProductionEnvironmentFile -Target $Target -EnvFile $EnvFile
    }
    finally {

        # These files contain source and configuration data. Remove them locally
        # whether the transfer succeeds or fails.
        if (Test-Path -LiteralPath $archive) {
            Remove-Item -LiteralPath $archive -Force
        }

        if (Test-Path -LiteralPath $manifest) {
            Remove-Item -LiteralPath $manifest -Force
        }
    }
}

# Uploads deployment/.env through a temporary remote filename, then moves it
# into place. The remote mv is atomic on one filesystem, so readers never see a
# partially copied environment file.
function Install-ProductionEnvironmentFile {
    param(
        [Parameter(Mandatory)]
        $Target,

        [Parameter(Mandatory)]
        [string]$EnvFile
    )

    Confirm-ProductionEnvironmentFile -Path $EnvFile

    $ssh = Get-RequiredCommandPath ssh
    $scp = Get-RequiredCommandPath scp
    $sshArguments = New-SshConnectionArguments -Target $Target
    $remoteTemporaryFile = "$($Target.Directory)/deployment/.env.upload-$([Guid]::NewGuid().ToString('N'))"
    $remoteTemporaryFileCreated = $false

    try {

        # Pre-create the destination with restrictive permissions so the upload
        # never leaves an environment file readable by other server users.
        & $ssh @sshArguments $Target.Address "mkdir -p '$($Target.Directory)/deployment' && install -m 600 /dev/null '$remoteTemporaryFile'"
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not prepare the remote environment file.'
        }

        $remoteTemporaryFileCreated = $true
        & $scp @sshArguments (Resolve-Path -LiteralPath $EnvFile).Path "$($Target.Address):$remoteTemporaryFile"
        if ($LASTEXITCODE -ne 0) {
            throw 'Production environment upload failed.'
        }

        & $ssh @sshArguments $Target.Address "chmod 600 '$remoteTemporaryFile' && mv -f '$remoteTemporaryFile' '$($Target.Directory)/deployment/.env'"
        if ($LASTEXITCODE -ne 0) {
            throw 'Production environment installation failed.'
        }

        $remoteTemporaryFileCreated = $false
    }
    finally {
        if ($remoteTemporaryFileCreated) {

            # Cleanup is deliberately best-effort. A cleanup failure must not
            # hide the original transfer error that caused this branch.
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & $ssh @sshArguments $Target.Address "rm -f '$remoteTemporaryFile'" 1>$null 2>$null
            }
            catch {

                # The original exception remains the useful diagnostic.
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
        }
    }
}

# Checks the two files that identify a complete checkout for this project. It
# prevents deploy and migration commands from running in an empty or wrong
# directory when synchronization was skipped.
function Confirm-RemoteDeploymentCheckout {
    param(
        [Parameter(Mandatory)]
        $Target
    )

    $configuration = $script:DeploymentConfiguration
    $ssh = Get-RequiredCommandPath ssh
    $sshArguments = New-SshConnectionArguments -Target $Target
    $projectMarkerPath = "$($Target.Directory)/$($configuration.RepositoryMarkerPath)"

    & $ssh @sshArguments $Target.Address "test -f '$($Target.Directory)/deployment/server/common.sh' -a -f '$projectMarkerPath'"
    if ($LASTEXITCODE -ne 0) {
        throw "Remote $($configuration.Name) checkout is incomplete. Run deployment/client/sync.ps1 first."
    }
}

# Validates the local production environment file before it is uploaded. The
# parser treats it as data, not as PowerShell or shell code, and rejects malformed
# or duplicate entries so deployment configuration remains unambiguous.
function Confirm-ProductionEnvironmentFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Environment file not found: $Path"
    }

    $values = @{}

    # Resolve-Path verifies the file and supplies its full path. ReadLines then
    # streams one line at a time, so `$line` contains each non-secret text line
    # from .env in turn without loading the whole file as one string.
    foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $Path).Path)) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) {
            continue
        }

        if ($trimmed -notmatch '^(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$') {
            throw "Invalid environment line: $trimmed"
        }

        $key = $Matches.key
        if ($values.ContainsKey($key)) {
            throw "Duplicate environment key: $key"
        }

        $values[$key] = $Matches.value.Trim()
    }

    # The configuration lists the keys this website cannot run without. During
    # each pass `$key` is one required name, which is checked against the values
    # parsed from .env above before anything is copied to the production server.
    foreach ($key in $script:DeploymentConfiguration.RequiredProductionEnvironmentKeys) {
        if (-not $values.ContainsKey($key)) {
            throw "Required production environment key is missing: $key"
        }

        if ([string]::IsNullOrWhiteSpace($values[$key]) -or $values[$key] -match '^(change-me|replace-me)') {
            throw "Required production environment key has no usable value: $key"
        }
    }
}
