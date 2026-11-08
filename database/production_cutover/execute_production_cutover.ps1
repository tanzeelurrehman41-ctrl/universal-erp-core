[CmdletBinding()]
param(
    [string]$TargetHost = "127.0.0.1",
    [int]$TargetPort = 5432,
    [string]$TargetDatabase = "universal_erp_prod",
    [string]$AdminUser = "erp_dev",
    [string]$AdminPassword = "",
    [string]$AppUser = "erp_app",
    [string]$AppPassword = "",
    [switch]$ConfirmExecution = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$MasterDdlPath = Join-Path $ScriptDir "01_production_schema_ddl.sql"
$AuditRlsPath = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) "audit_rls_policies.sql"

Write-Host "=========================================================================="
Write-Host "=== Universal ERP Production Cutover & Schema Deployment Execution ==="
Write-Host "=========================================================================="

if (-not (Test-Path $MasterDdlPath)) {
    throw "CRITICAL: Master production DDL package not found at $MasterDdlPath"
}

$masterDdlHash = (Get-FileHash -Path $MasterDdlPath -Algorithm SHA256).Hash
Write-Host "Target Host:        $TargetHost"
Write-Host "Target Port:        $TargetPort"
Write-Host "Target Database:    $TargetDatabase"
Write-Host "Target Admin User:  $AdminUser"
Write-Host "Master DDL SHA256:  $masterDdlHash"
Write-Host ""

if (-not $ConfirmExecution) {
    Write-Host "[SAFETY GUARD ACTIVE] -ConfirmExecution switch not passed." -ForegroundColor Yellow
    Write-Host "No changes were made to the target environment." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To execute live cutover after receiving explicit stakeholder authorization, run:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`" -ConfirmExecution" -ForegroundColor Cyan
    exit 0
}

Write-Host "[CONFIRMED] Stakeholder authorization received. Initiating cutover..." -ForegroundColor Green

# 1. Verify Connectivity
Write-Host "`n[1/5] Verifying PostgreSQL connection to ${TargetHost}:${TargetPort}/${TargetDatabase}..."
$env:PGPASSWORD = $AdminPassword
psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -c "SELECT version();"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to connect to target production database."
}

# 2. Apply Master DDL within Transaction
Write-Host "`n[2/5] Applying Master DDL Package (001-022)..."
Get-Content $MasterDdlPath -Raw | psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "Master DDL execution failed. Transaction rolled back automatically."
}
Write-Host "[PASS] Master DDL package successfully applied." -ForegroundColor Green

# 3. Verify Migration Ledger
Write-Host "`n[3/5] Verifying production migration ledger..."
$ledgerCountRaw = psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -t -c "SELECT count(*) FROM public.schema_migrations;"
$ledgerCount = ($ledgerCountRaw | Out-String).Trim()
Write-Host "Migrations recorded: $ledgerCount / 22"
if ([int]$ledgerCount -ne 22) {
    throw "Migration count mismatch in production ledger: expected 22, got $ledgerCount"
}

# 4. Execute Gate 11 RLS Policy Audit
Write-Host "`n[4/5] Running Gate 11 Row-Level Security Audit on production database..."
$rlsViolations = Get-Content $AuditRlsPath -Raw | psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -t -A -v ON_ERROR_STOP=1
$rlsViolationsClean = ($rlsViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($rlsViolationsClean)) {
    throw "CRITICAL: Gate 11 RLS Policy Audit failed on production: $rlsViolationsClean"
}
Write-Host "[PASS] Gate 11 RLS Audit Verified: 0 policy violations in production." -ForegroundColor Green

# 5. Create Baseline Cryptographic Backup of Production DB
Write-Host "`n[5/5] Generating baseline production backup (pg_dump -Fc)..."
$prodBackupFile = Join-Path $ScriptDir "universal_erp_prod_live_baseline_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump"
pg_dump -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -Fc -f $prodBackupFile
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create baseline dump of production database."
}
$prodBackupHash = (Get-FileHash -Path $prodBackupFile -Algorithm SHA256).Hash
Write-Host "[PASS] Live production baseline backup created: $prodBackupFile" -ForegroundColor Green
Write-Host "       SHA256: $prodBackupHash" -ForegroundColor Green

Write-Host "`n=========================================================================="
Write-Host "=== PRODUCTION DATABASE CUTOVER SUCCESSFULLY COMPLETED ==="
Write-Host "=========================================================================="
