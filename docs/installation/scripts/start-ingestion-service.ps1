#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and starts the ERP Error Management ingestion service.

.DESCRIPTION
    Restores NuGet packages, builds the ingestion service, and starts it on the
    configured URL. The service applies any pending database migrations at startup.

.PARAMETER Environment
    ASP.NET Core environment name. Defaults to Development.

.PARAMETER Url
    Listening URL. Defaults to http://localhost:5080.

.PARAMETER NoBuild
    Skip the build step and run the last compiled output.

.EXAMPLE
    .\start-ingestion-service.ps1
    .\start-ingestion-service.ps1 -Environment Production -Url "http://0.0.0.0:8080"
#>
param(
    [string]$Environment = "Development",
    [string]$Url         = "http://localhost:5080",
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Resolve-Path (Join-Path $scriptDir "../../..")
$projectDir = Join-Path $repoRoot "src\Company.ErrorManagement.IngestionService"

Write-Host ""
Write-Host "=== ERP Error Management — Ingestion Service ===" -ForegroundColor Cyan
Write-Host "  Project     : $projectDir"
Write-Host "  Environment : $Environment"
Write-Host "  URL         : $Url"
Write-Host ""

# Restore and build
if (-not $NoBuild) {
    Write-Host "  Restoring packages..." -ForegroundColor Yellow
    dotnet restore $projectDir
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet restore failed"; exit 1 }

    Write-Host "  Building..." -ForegroundColor Yellow
    dotnet build $projectDir --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed"; exit 1 }
}

Write-Host ""
Write-Host "  Starting ingestion service on $Url ..." -ForegroundColor Green
Write-Host "  Swagger UI: $Url/swagger" -ForegroundColor Green
Write-Host "  Health:     $Url/health" -ForegroundColor Green
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:ASPNETCORE_URLS        = $Url

dotnet run `
    --project $projectDir `
    --configuration Release `
    --no-build
