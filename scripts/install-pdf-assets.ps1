[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $PSScriptRoot "..\runtime\pdf-assets")
)

$ErrorActionPreference = "Stop"
$assetRoot = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $assetRoot) {
    throw "The PDF asset directory already exists: $assetRoot"
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("gaifulinlab-mathjax-" + [guid]::NewGuid().ToString("N"))
$installed = $false
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    New-Item -ItemType Directory -Path $assetRoot | Out-Null
    $mathJaxArchive = Join-Path $temporaryDirectory "mathjax.tgz"
    $fontArchive = Join-Path $temporaryDirectory "mathjax-newcm-font.tgz"

    curl.exe -fL "https://registry.npmjs.org/mathjax/-/mathjax-4.1.3.tgz" -o $mathJaxArchive
    tar.exe -xzf $mathJaxArchive -C $temporaryDirectory
    Move-Item -LiteralPath (Join-Path $temporaryDirectory "package") -Destination (Join-Path $assetRoot "mathjax")

    curl.exe -fL "https://registry.npmjs.org/@mathjax/mathjax-newcm-font/-/mathjax-newcm-font-4.1.3.tgz" -o $fontArchive
    tar.exe -xzf $fontArchive -C $temporaryDirectory
    Move-Item -LiteralPath (Join-Path $temporaryDirectory "package") -Destination (Join-Path $assetRoot "mathjax-newcm-font")
    $installed = $true
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $installed -and (Test-Path -LiteralPath $assetRoot)) {
        Remove-Item -LiteralPath $assetRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
