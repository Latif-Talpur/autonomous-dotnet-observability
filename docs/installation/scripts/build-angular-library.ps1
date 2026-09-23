#Requires -Version 5.1
<#
.SYNOPSIS
    Installs dependencies and builds the Angular error management library.

.DESCRIPTION
    Runs npm install and tsc compilation inside src/angular/erp-error-angular.
    Output goes to src/angular/erp-error-angular/dist/.

.EXAMPLE
    .\build-angular-library.ps1
#>
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Resolve-Path (Join-Path $scriptDir "../../..")
$libraryDir = Join-Path $repoRoot "src\angular\erp-error-angular"

Write-Host ""
Write-Host "=== ERP Error Management — Angular Library Build ===" -ForegroundColor Cyan
Write-Host "  Library : $libraryDir"
Write-Host ""

Push-Location $libraryDir
try {
    Write-Host "  Installing npm dependencies..." -ForegroundColor Yellow
    npm install
    if ($LASTEXITCODE -ne 0) { Write-Error "npm install failed"; exit 1 }

    Write-Host "  Compiling TypeScript..." -ForegroundColor Yellow
    npm run build
    if ($LASTEXITCODE -ne 0) { Write-Error "npm run build failed"; exit 1 }

    Write-Host ""
    Write-Host "Angular library build succeeded." -ForegroundColor Green
    Write-Host "Output: $libraryDir\dist" -ForegroundColor Green
} finally {
    Pop-Location
}
