#Requires -Version 5.1
<#
.SYNOPSIS
    Restores and builds the full .NET solution.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    .\build-solution.ps1
    .\build-solution.ps1 -Configuration Debug
#>
param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir "../../..")
$solution  = Join-Path $repoRoot "AutonomousDotNetObservability.sln"

Write-Host ""
Write-Host "=== ERP Error Management — Build ===" -ForegroundColor Cyan
Write-Host "  Solution      : $solution"
Write-Host "  Configuration : $Configuration"
Write-Host ""

Write-Host "  Restoring packages..." -ForegroundColor Yellow
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet restore failed"; exit 1 }

Write-Host "  Building..." -ForegroundColor Yellow
dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed"; exit 1 }

Write-Host ""
Write-Host "Build succeeded." -ForegroundColor Green
