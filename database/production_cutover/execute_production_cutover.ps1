[CmdletBinding()]
param(
    [string]$TargetHost = "127.0.0.1",
    [int]$TargetPort = 5432,
    [string]$TargetDatabase = "universal_erp",
    [string]$AdminUser = "postgres",
    [string]$AdminPassword = "",
    [switch]$ConfirmExecution = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$IncrementalCutoverPath = Join-Path $ScriptDir "03_cutover_universal_erp_m021_m022.sql"
$FreshMasterDdlPath = Join-Path $ScriptDir "01_production_schema_ddl.sql"
$AuditRlsPath = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) "audit_rls_policies.sql"
$BackupsDir = "C:\backups"

$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$pg_dump = "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
if (-not (Test-Path $psql)) { $psql = "psql" }
if (-not (Test-Path $pg_dump)) { $pg_dump = "pg_dump" }

Write-Host "=========================================================================="
Write-Host "=== Universal ERP Production Cutover & Schema Deployment Execution ==="
Write-Host "=========================================================================="

Write-Host "Target Host:        $TargetHost"
Write-Host "Target Port:        $TargetPort"
Write-Host "Target Database:    $TargetDatabase"
Write-Host "Target Admin User:  $AdminUser"
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

if ($AdminPassword) { $env:PGPASSWORD = $AdminPassword }

# 1. Verify Connectivity and State
Write-Host "`n[1/6] Verifying connection to ${TargetHost}:${TargetPort}/${TargetDatabase}..."
$ver = & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -t -A -c "SELECT current_database(), version();"
if ($LASTEXITCODE -ne 0) { throw "Failed to connect to target production database." }
Write-Host "Connected: $ver" -ForegroundColor Green

# 2. Check Existing Migration Count
$ledgerCountRaw = & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -t -A -c "SELECT count(*) FROM public.schema_migrations;"
$initialLedgerCount = if ($LASTEXITCODE -eq 0 -and $ledgerCountRaw) { [int]$ledgerCountRaw.Trim() } else { 0 }
Write-Host "Initial recorded migrations: $initialLedgerCount"

# 3. Capture Pre-Deployment Snapshot
Write-Host "`n[2/6] Capturing Pre-Cutover Backup of $TargetDatabase..."
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$preDumpPath = Join-Path $BackupsDir "production_${TargetDatabase}_pre_cutover_${ts}.dump"
& $pg_dump -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -Fc -f $preDumpPath
if ($LASTEXITCODE -ne 0) { throw "Pre-deployment pg_dump failed!" }
$preHash = (Get-FileHash -Path $preDumpPath -Algorithm SHA256).Hash
$preSize = (Get-Item $preDumpPath).Length
Write-Host "[PASS] Pre-cutover backup created: $preDumpPath ($preSize bytes, SHA256: $preHash)" -ForegroundColor Green

# 4. Apply Deployment Package
if ($initialLedgerCount -ge 20) {
    Write-Host "`n[3/6] Applying Incremental Cutover (Migrations 021 & 022) under advisory lock 714209..."
    & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -v ON_ERROR_STOP=1 -f $IncrementalCutoverPath
    if ($LASTEXITCODE -ne 0) { throw "Incremental cutover failed. Transaction rolled back." }
} else {
    Write-Host "`n[3/6] Applying Full Fresh Master DDL Package (Migrations 001-022)..."
    Get-Content $FreshMasterDdlPath -Raw | & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw "Master DDL failed. Transaction rolled back." }
}
Write-Host "[PASS] Database migration committed successfully." -ForegroundColor Green

# 5. Verify Ledger and Forced RLS
Write-Host "`n[4/6] Verifying schema migrations ledger..."
$finalLedgerCountRaw = & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -t -A -c "SELECT count(*) FROM public.schema_migrations;"
$finalLedgerCount = [int]$finalLedgerCountRaw.Trim()
Write-Host "Final migrations recorded: $finalLedgerCount / 22"
if ($finalLedgerCount -ne 22) { throw "Expected 22 migrations, got $finalLedgerCount" }

Write-Host "`n[5/6] Executing Gate 11 RLS Policy Audit on production database..."
$rlsViolations = Get-Content $AuditRlsPath -Raw | & $psql -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -w -t -A -v ON_ERROR_STOP=1
$rlsViolationsClean = ($rlsViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($rlsViolationsClean)) {
    throw "CRITICAL: Gate 11 RLS Audit failed with violations: $rlsViolationsClean"
}
Write-Host "[PASS] Gate 11 RLS Audit Verified: 0 policy violations." -ForegroundColor Green

# 6. Post-Deployment Snapshot
Write-Host "`n[6/6] Generating Post-Cutover Baseline Snapshot..."
$postDumpPath = Join-Path $BackupsDir "production_${TargetDatabase}_post_cutover_${ts}.dump"
& $pg_dump -h $TargetHost -p $TargetPort -U $AdminUser -d $TargetDatabase -Fc -f $postDumpPath
if ($LASTEXITCODE -ne 0) { throw "Post-cutover pg_dump failed!" }
$postHash = (Get-FileHash -Path $postDumpPath -Algorithm SHA256).Hash
$postSize = (Get-Item $postDumpPath).Length
Write-Host "[PASS] Post-cutover baseline snapshot created: $postDumpPath ($postSize bytes, SHA256: $postHash)" -ForegroundColor Green

Write-Host "`n=========================================================================="
Write-Host "=== PRODUCTION DATABASE CUTOVER COMPLETED SUCCESSFULLY ==="
Write-Host "=========================================================================="
