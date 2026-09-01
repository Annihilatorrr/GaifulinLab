[CmdletBinding()]
param()

# Starts the only local Docker dependency: the PDF worker.
# API, Web, and PostgreSQL continue to run from Visual Studio as usual.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'docker-compose.local.yml'
$pdfAssetsPath = Join-Path $repositoryRoot 'runtime\pdf-assets'
$mathJaxPath = Join-Path $pdfAssetsPath 'mathjax\tex-chtml.js'
$mathJaxFontPath = Join-Path $pdfAssetsPath 'mathjax-newcm-font'

if (-not (Test-Path -LiteralPath $pdfAssetsPath)) {
    Write-Host 'Installing local MathJax assets for PDF rendering...'
    & (Join-Path $PSScriptRoot 'install-pdf-assets.ps1') -Destination $pdfAssetsPath
}

if (-not (Test-Path -LiteralPath $mathJaxPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $mathJaxFontPath -PathType Container)) {
    throw "The local PDF assets are incomplete at '$pdfAssetsPath'. Remove that directory and run this script again."
}

# The API started later from this PowerShell session inherits these paths.
$env:PDF_MATHJAX_ASSETS_PATH = [System.IO.Path]::GetFullPath($pdfAssetsPath)
$env:PDF_MATHJAX_PATH = [System.IO.Path]::GetFullPath($mathJaxPath)

Write-Host "Using local MathJax assets from '$env:PDF_MATHJAX_ASSETS_PATH'."

# Clear stale containers from earlier versions of the local Compose project.
# This project contains only the worker and has no Docker-managed data volumes.
docker compose -f $composeFile down --remove-orphans
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

docker compose -f $composeFile up --detach --build --remove-orphans
exit $LASTEXITCODE
