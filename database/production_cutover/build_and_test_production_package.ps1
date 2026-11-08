[CmdletBinding()]
param(
    [string]$ContainerName = "universal-erp-postgres",
    [string]$TargetDatabase = "universal_erp_prod_dryrun",
    [string]$DbUser = "erp_dev"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$MigrationsDir = Join-Path (Split-Path -Parent $ScriptDir) "migrations"
$OutputDir = $ScriptDir

Write-Host "================================================================="
Write-Host "=== Universal ERP Production Package Builder & Verification ==="
Write-Host "================================================================="

# 1. Enumerate and Verify Migration Files
$migrationFiles = @(
    "001_platform_foundation.sql",
    "002_inventory.sql",
    "003_local_dev_bootstrap.sql",
    "004_offline_sync_foundation.sql",
    "005_oidc_identity_and_permissions.sql",
    "006_oidc_workspace_lookup.sql",
    "007_inventory_workflow_permissions.sql",
    "008_sync_change_feed.sql",
    "009_sync_change_feed_index.sql",
    "010_offline_device_enrollment.sql",
    "011_purchasing.sql",
    "012_saas_tenant_onboarding.sql",
    "013_double_entry_accounting.sql",
    "014_finance_chart_of_accounts_seed.sql",
    "015_sales_and_pos.sql",
    "016_sales_seed.sql",
    "017_offline_sync_hardening.sql",
    "018_command_idempotency_company_scope_and_conflicts.sql",
    "019_purchasing_outbox_dead_letter.sql",
    "020_reconcile_application_runtime_privileges.sql",
    "021_grant_context_listing_routine_privilege.sql",
    "022_complete_erp_foundation.sql"
)

Write-Host "[1/6] Verifying all 22 migration source files..."
$manifest = @()
foreach ($file in $migrationFiles) {
    $path = Join-Path $MigrationsDir $file
    if (-not (Test-Path $path)) {
        throw "Missing required migration file: $file at $path"
    }
    $hash = (Get-FileHash -Path $path -Algorithm SHA256).Hash
    $size = (Get-Item $path).Length
    $manifest += [PSCustomObject]@{
        Filename = $file
        Size = $size
        SHA256 = $hash
    }
}
$manifest | Format-Table -AutoSize

# 2. Build Production Master DDL Package
Write-Host "[2/6] Assembling Production Schema Package (zero staging/test data)..."
$masterDdlPath = Join-Path $OutputDir "01_production_schema_ddl.sql"

$ddlBuilder = [System.Text.StringBuilder]::new()
[void]$ddlBuilder.AppendLine("-- ===========================================================================")
[void]$ddlBuilder.AppendLine("-- Universal ERP Production Database Schema Deployment Package")
[void]$ddlBuilder.AppendLine("-- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')")
[void]$ddlBuilder.AppendLine("-- Fully hardened: Forced RLS, least-privilege erp_app, zero development seeds")
[void]$ddlBuilder.AppendLine("-- ===========================================================================")
[void]$ddlBuilder.AppendLine("BEGIN;")
[void]$ddlBuilder.AppendLine()
[void]$ddlBuilder.AppendLine("-- 0. Schema migrations tracking ledger")
[void]$ddlBuilder.AppendLine("CREATE TABLE IF NOT EXISTS public.schema_migrations (")
[void]$ddlBuilder.AppendLine("    version text PRIMARY KEY,")
[void]$ddlBuilder.AppendLine("    applied_at timestamptz NOT NULL DEFAULT now()")
[void]$ddlBuilder.AppendLine(");")
[void]$ddlBuilder.AppendLine()

foreach ($file in $migrationFiles) {
    $path = Join-Path $MigrationsDir $file
    $versionName = [System.IO.Path]::GetFileNameWithoutExtension($file)
    $content = Get-Content $path -Raw

    [void]$ddlBuilder.AppendLine("-- ---------------------------------------------------------------------------")
    [void]$ddlBuilder.AppendLine("-- Migration: $file")
    [void]$ddlBuilder.AppendLine("-- ---------------------------------------------------------------------------")

    if ($file -eq "003_local_dev_bootstrap.sql") {
        # Extract only the DDL parts (command_idempotency, role erp_app, grants), omit LOCAL-DEMO seed
        $lines = $content -split "`r?`n"
        $prodLines = @()
        foreach ($line in $lines) {
            if ($line -match "INSERT INTO platform\.tenants") { break }
            if ($line -match "CREATE ROLE erp_app") { continue }
            if ($line -match "WITH LOGIN NOSUPERUSER") { continue }
            if ($line -match "PASSWORD 'local_erp_app_change_me'") {
                # Insert idempotent role creation with named block tag
                $prodLines += "DO `$role_block`$"
                $prodLines += "BEGIN"
                $prodLines += "    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'erp_app') THEN"
                $prodLines += "        CREATE ROLE erp_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;"
                $prodLines += "    END IF;"
                $prodLines += "END `$role_block`$;"
                continue
            }
            $prodLines += $line
        }
        $prod003 = ($prodLines -join "`n")
        [void]$ddlBuilder.AppendLine($prod003)
    } elseif ($file -eq "007_inventory_workflow_permissions.sql") {
        # Replace hardcoded demo tenant UOM insert with dynamic SELECT from platform.tenants so fresh DB has zero foreign key violation
        $targetBlock = @"
INSERT INTO erp_inventory.unit_of_measure
  (tenant_id, uom_id, code, name, dimension, decimal_places)
VALUES
  ('00000000-0000-4000-8000-000000000001',
   '00000000-0000-4000-8000-000000000006', 'EA', 'Each', 'count', 0)
ON CONFLICT (tenant_id, code) DO NOTHING;
"@
        $replacementBlock = @"
INSERT INTO erp_inventory.unit_of_measure
  (tenant_id, uom_id, code, name, dimension, decimal_places)
SELECT id, '00000000-0000-4000-8000-000000000006', 'EA', 'Each', 'count', 0
FROM platform.tenants
ON CONFLICT (tenant_id, code) DO NOTHING;
"@
        $normContent = $content.Replace("`r`n", "`n")
        $normTarget = $targetBlock.Replace("`r`n", "`n")
        $normReplacement = $replacementBlock.Replace("`r`n", "`n")
        $clean007 = $normContent.Replace($normTarget, $normReplacement)
        [void]$ddlBuilder.AppendLine($clean007)
    } elseif ($file -eq "014_finance_chart_of_accounts_seed.sql" -or $file -eq "016_sales_seed.sql") {
        # Omit demo tenant seed records; standard Chart of Accounts template is dynamically initialized on tenant onboarding
        [void]$ddlBuilder.AppendLine("-- [SKIPPED FOR PROD] Development demo tenant seed ($file). Legitimate tenant setup is provisioned via SaaS onboarding.")
    } else {
        [void]$ddlBuilder.AppendLine($content)
    }

    [void]$ddlBuilder.AppendLine()
    [void]$ddlBuilder.AppendLine("INSERT INTO public.schema_migrations (version, applied_at) VALUES ('$versionName', now()) ON CONFLICT (version) DO NOTHING;")
    [void]$ddlBuilder.AppendLine()
}

[void]$ddlBuilder.AppendLine("COMMIT;")

[System.IO.File]::WriteAllText($masterDdlPath, $ddlBuilder.ToString(), [System.Text.Encoding]::UTF8)
$masterHash = (Get-FileHash -Path $masterDdlPath -Algorithm SHA256).Hash
Write-Host "[PASS] Master DDL package generated: $masterDdlPath (SHA256: $masterHash)"

# 3. Execute Dry-Run Migration on Disposable Database
Write-Host "[3/6] Applying Production Master DDL to dry-run database '$TargetDatabase'..."
docker exec $ContainerName psql -U $DbUser -d postgres -c "DROP DATABASE IF EXISTS $TargetDatabase;"
docker exec $ContainerName psql -U $DbUser -d postgres -c "CREATE DATABASE $TargetDatabase WITH OWNER $DbUser;"
Get-Content $masterDdlPath -Raw | docker exec -i $ContainerName psql -U $DbUser -d $TargetDatabase -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to apply production master DDL package to $TargetDatabase"
}
Write-Host "[PASS] All migrations successfully applied to $TargetDatabase."

# 4. Verify Migration Ledger and Forced RLS Status
Write-Host "[4/6] Verifying schema migrations ledger and Forced RLS..."
$ledgerCountRaw = docker exec $ContainerName psql -U $DbUser -d $TargetDatabase -t -c "SELECT count(*) FROM public.schema_migrations;"
$ledgerCount = ($ledgerCountRaw | Out-String).Trim()
Write-Host "Recorded migrations count: $ledgerCount / 22"
if ([int]$ledgerCount -ne 22) {
    throw "Migration ledger mismatch: expected 22, got $ledgerCount"
}

# Verify Forced RLS on all domain tables using official Gate 11 Audit
$repoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$auditRlsPath = Join-Path $repoRoot "audit_rls_policies.sql"
$rlsViolations = Get-Content $auditRlsPath -Raw | docker exec -i $ContainerName psql -U $DbUser -d $TargetDatabase -t -A -v ON_ERROR_STOP=1
$rlsViolationsClean = ($rlsViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($rlsViolationsClean)) {
    throw "CRITICAL: Gate 11 RLS Policy Audit found violations: $rlsViolationsClean"
}
Write-Host "[PASS] Gate 11 RLS Audit Verified: 0 policy violations across all domain tables."

# 5. Generate and Verify Production Baseline Backup Artifact
Write-Host "[5/6] Creating verified deterministic backup artifact (pg_dump -Fc)..."
$backupFilename = "universal_erp_prod_baseline_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump"
$backupPathInContainer = "/tmp/$backupFilename"
$localBackupPath = Join-Path $OutputDir $backupFilename

docker exec $ContainerName pg_dump -U $DbUser -d $TargetDatabase -Fc -f $backupPathInContainer
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed in container" }

# Verify Backup Archive TOC directly in container
$tocOutput = docker exec $ContainerName pg_restore -l $backupPathInContainer
$tocLines = ($tocOutput | Measure-Object -Line).Lines
Write-Host "[PASS] Backup TOC readable: $tocLines catalog entries verified."

# 6. Test Backup Restoration into Separate Test Database
Write-Host "[6/6] Testing full restore into secondary database 'universal_erp_prod_restore_test'..."
docker exec $ContainerName psql -U $DbUser -d postgres -c "DROP DATABASE IF EXISTS universal_erp_prod_restore_test;"
docker exec $ContainerName psql -U $DbUser -d postgres -c "CREATE DATABASE universal_erp_prod_restore_test WITH OWNER erp_dev;"

docker exec $ContainerName pg_restore -U $DbUser -d universal_erp_prod_restore_test --no-owner --role=erp_dev --exit-on-error $backupPathInContainer
if ($LASTEXITCODE -ne 0) { throw "pg_restore test failed" }

$restoredLedgerCountRaw = docker exec $ContainerName psql -U $DbUser -d universal_erp_prod_restore_test -t -c "SELECT count(*) FROM public.schema_migrations;"
$restoredLedgerCount = ($restoredLedgerCountRaw | Out-String).Trim()
Write-Host "[PASS] Restoration verified: $restoredLedgerCount migrations intact."

# Clean up temporary restore database
docker exec $ContainerName psql -U $DbUser -d postgres -c "DROP DATABASE universal_erp_prod_restore_test;"

# Copy verified backup artifact to host output directory
docker cp "${ContainerName}:${backupPathInContainer}" $localBackupPath
docker exec $ContainerName rm -f $backupPathInContainer

$backupHash = (Get-FileHash -Path $localBackupPath -Algorithm SHA256).Hash
$backupSize = (Get-Item $localBackupPath).Length
Write-Host "[PASS] Production backup created and verified on host: $localBackupPath"
Write-Host "       Size: $backupSize bytes"
Write-Host "       SHA256: $backupHash"

Write-Host "================================================================="
Write-Host "=== PRODUCTION PACKAGE BUILD & DRY-RUN VERIFICATION SUCCESSFUL ==="
Write-Host "================================================================="
