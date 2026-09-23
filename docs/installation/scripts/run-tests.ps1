#Requires -Version 5.1
<#
.SYNOPSIS
    Runs all automated tests for the ERP Error Management framework.

.DESCRIPTION
    Runs unit and integration tests across Core, Persistence.Sqlite, and
    AspNetCore test projects. Integration tests create and destroy temporary
    SQLite databases automatically.

.PARAMETER Filter
    Optional test filter expression passed to dotnet test --filter.

.PARAMETER Configuration
    Build configuration. Defaults to Debug.

.EXAMPLE
    .\run-tests.ps1
    .\run-tests.ps1 -Filter "FullyQualifiedName~FingerprintProvider"
    .\run-tests.ps1 -Configuration Release
#>
param(
    [string]$Filter        = "",
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir "../../..")
$solution  = Join-Path $repoRoot "AutonomousDotNetObservability.sln"

Write-Host ""
Write-Host "=== ERP Error Management — Test Runner ===" -ForegroundColor Cyan
Write-Host "  Solution      : $solution"
Write-Host "  Configuration : $Configuration"
if ($Filter) { Write-Host "  Filter        : $Filter" }
Write-Host ""

# Build first
Write-Host "  Building solution..." -ForegroundColor Yellow
$buildArgs = @("build", $solution, "--configuration", $Configuration, "--no-incremental")
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed. Fix compilation errors before running tests."; exit 1 }

Write-Host ""

$testProjects = @(
    "tests\Company.ErrorManagement.Core.Tests",
    "tests\Company.ErrorManagement.Persistence.Sqlite.Tests",
    "tests\Company.ErrorManagement.AspNetCore.Tests"
)

$passed = 0
$failed = 0

foreach ($proj in $testProjects) {
    $projPath = Join-Path $repoRoot $proj
    $projName = Split-Path -Leaf $proj
    Write-Host "  Running $projName ..." -ForegroundColor Yellow

    $testArgs = @(
        "test", $projPath,
        "--configuration", $Configuration,
        "--no-build",
        "--logger", "console;verbosity=minimal"
    )
    if ($Filter) { $testArgs += @("--filter", $Filter) }

    dotnet @testArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  PASSED: $projName" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "  FAILED: $projName" -ForegroundColor Red
        $failed++
    }
    Write-Host ""
}

Write-Host "=== Results: $passed passed, $failed failed ===" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

if ($failed -gt 0) { exit 1 }
