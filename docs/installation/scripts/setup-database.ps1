#Requires -Version 5.1
<#
.SYNOPSIS
    Creates and initialises the ERP Error Management SQLite database.

.DESCRIPTION
    - Creates erp_error_management.db in the repo root (or a custom path).
    - Applies migrations V001 and V002.
    - Seeds reference data (environments, severities, queues, statuses, SLA policies).

.PARAMETER DbPath
    Path to the SQLite database file. Defaults to <repo-root>/erp_error_management.db.

.PARAMETER SqliteCli
    Path to the sqlite3 executable. If not provided, the script looks for it on PATH.

.EXAMPLE
    .\setup-database.ps1
    .\setup-database.ps1 -DbPath "C:\data\erp.db"
#>
param(
    [string]$DbPath = "",
    [string]$SqliteCli = "sqlite3"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Locate repo root (parent of docs/installation/scripts)
$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Resolve-Path (Join-Path $scriptDir "../../..")

if ([string]::IsNullOrWhiteSpace($DbPath)) {
    $DbPath = Join-Path $repoRoot "erp_error_management.db"
}

$migrationsDir = Join-Path $repoRoot "database\migrations"
$seedDir       = Join-Path $repoRoot "database\seed"

Write-Host ""
Write-Host "=== ERP Error Management — Database Setup ===" -ForegroundColor Cyan
Write-Host "  Repo root  : $repoRoot"
Write-Host "  DB file    : $DbPath"
Write-Host "  Migrations : $migrationsDir"
Write-Host "  Seed       : $seedDir"
Write-Host ""

# Check sqlite3 availability
try {
    $version = & $SqliteCli "--version" 2>&1
    Write-Host "  sqlite3    : $version" -ForegroundColor Green
} catch {
    Write-Error "sqlite3 not found on PATH. Install SQLite or pass -SqliteCli <path>."
    exit 1
}

# Apply migrations in order
$migrations = Get-ChildItem -Path $migrationsDir -Filter "V*.sql" | Sort-Object Name
foreach ($migration in $migrations) {
    Write-Host "  Applying   : $($migration.Name)" -ForegroundColor Yellow
    $sql = Get-Content $migration.FullName -Raw
    $sql | & $SqliteCli $DbPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Migration failed: $($migration.Name)"
        exit 1
    }
}

# Apply seed files
$seeds = Get-ChildItem -Path $seedDir -Filter "*.sql" | Sort-Object Name
foreach ($seed in $seeds) {
    Write-Host "  Seeding    : $($seed.Name)" -ForegroundColor Yellow
    $sql = Get-Content $seed.FullName -Raw
    $sql | & $SqliteCli $DbPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Seed failed: $($seed.Name)"
        exit 1
    }
}

# Verify
Write-Host ""
Write-Host "  Schema versions applied:" -ForegroundColor Cyan
$result = & $SqliteCli $DbPath "SELECT version || ' - ' || description FROM schema_version ORDER BY version;"
Write-Host $result -ForegroundColor Green

Write-Host ""
Write-Host "Database setup complete." -ForegroundColor Green
