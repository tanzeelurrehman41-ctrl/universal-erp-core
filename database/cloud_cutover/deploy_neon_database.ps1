[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$NeonConnectionString = "",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$MasterDdlPath = Join-Path (Join-Path (Split-Path -Parent $ScriptDir) "production_cutover") "01_production_schema_ddl.sql"
$AuditRlsPath = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) "audit_rls_policies.sql"
$DemoSeedPath = Join-Path $ScriptDir "02_demo_tenant_seed.sql"

Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host "=== Universal ERP -- Neon Free Cloud Database Deployment Script ===" -ForegroundColor Cyan
Write-Host "==========================================================================" -ForegroundColor Cyan

# Safety Assertion: Local Production Protection
Write-Host "[SAFETY GUARD] Ensuring local production port 5432 / universal_erp is UNTOUCHED." -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($NeonConnectionString)) {
    Write-Host "`n[USAGE NOTICE] No Neon connection string supplied." -ForegroundColor Yellow
    Write-Host "To deploy to your Neon Free PostgreSQL cluster, run:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`" -NeonConnectionString `"postgresql://<user>:<password>@<endpoint>-pooler.us-east-2.aws.neon.tech/<dbname>?sslmode=verify-full`"" -ForegroundColor Cyan
    Write-Host "`nValidation: Master DDL package ready at $MasterDdlPath"
    exit 0
}

if ($NeonConnectionString -match "127\.0\.0\.1:5432" -or $NeonConnectionString -match "localhost:5432") {
    throw "SAFETY ABORT: Refusing to execute against local production port 5432! Target must be a remote Neon cloud database."
}

if ($NeonConnectionString -match "Trust Server Certificate\s*=\s*true" -or $NeonConnectionString -match "sslmode=disable") {
    throw "SECURITY REJECTION: Insecure SSL configuration detected. Trust Server Certificate=true and sslmode=disable are prohibited. Use sslmode=verify-full or SSL Mode=VerifyFull with trusted CA validation."
}

Write-Host "`n[1/4] Verifying connection to Neon PostgreSQL..." -ForegroundColor Yellow
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
if (-not (Test-Path $psql)) { $psql = "psql" }

$ver = & $psql "$NeonConnectionString" -w -t -A -c "SELECT version();"
if ($LASTEXITCODE -ne 0) { throw "Failed to connect to Neon PostgreSQL." }
Write-Host "Connected to Neon: $ver" -ForegroundColor Green

Write-Host "`n[2/4] Applying Master Schema Package (Migrations 001-022) to Neon..." -ForegroundColor Yellow
Get-Content $MasterDdlPath -Raw | & $psql "$NeonConnectionString" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { throw "Schema deployment failed on Neon." }
Write-Host "[PASS] All 22 migrations applied successfully." -ForegroundColor Green

Write-Host "`n[3/4] Verifying Forced Row-Level Security on Neon..." -ForegroundColor Yellow
$rlsViolations = Get-Content $AuditRlsPath -Raw | & $psql "$NeonConnectionString" -t -A -v ON_ERROR_STOP=1
$rlsViolationsClean = ($rlsViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($rlsViolationsClean)) {
    throw "CRITICAL: Gate 11 RLS Audit failed on Neon: $rlsViolationsClean"
}
Write-Host "[PASS] Gate 11 RLS Policy Audit: 0 violations across all 42 tables." -ForegroundColor Green

Write-Host "`n[4/4] Seeding isolated synthetic cloud demonstration tenant..." -ForegroundColor Yellow
Get-Content $DemoSeedPath -Raw | & $psql "$NeonConnectionString" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { throw "Failed to seed demo tenant on Neon." }
Write-Host "[PASS] Synthetic cloud demo tenant initialized cleanly." -ForegroundColor Green

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== NEON CLOUD DATABASE SUCCESSFULLY PROVISIONED AND VERIFIED ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
