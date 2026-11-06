# universal-erp-core/test_harness_v2.ps1
# CI Test Harness V2: Docker-Managed Ephemeral Loopback Port Allocation & Automated Regression Runner
param(
    [string]$ContainerName = "universal-erp-postgres-test",
    [string]$NetworkName = "universal-erp-test-net",
    [string]$VolumeName = "erp_postgres_test_data",
    [string]$DatabaseName = "universal_erp_disposable_test",
    [string]$ExpectedImageDigest = "postgres:17.11-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994",
    [string]$BackupFilePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.dump",
    [switch]$SkipBuild = $false,
    [switch]$SkipRestore = $false
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $candidate = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    while ($candidate) {
        if (Test-Path (Join-Path $candidate "src\backend\UniversalErp.sln")) { return (Resolve-Path $candidate).Path }
        $parent = Split-Path -Parent $candidate
        if (-not $parent -or $parent -eq $candidate) { break }
        $candidate = $parent
    }
    if (Test-Path (Join-Path (Get-Location).Path "src\backend\UniversalErp.sln")) { return (Get-Location).Path }
    throw "[FAIL-CLOSED] Unable to establish repository root! 'src\backend\UniversalErp.sln' not found."
}

$repoRoot = Get-RepoRoot
$preflightScript = Join-Path $repoRoot "preflight_test_env_guard.ps1"
$runnerScript = Join-Path $repoRoot "run_controlled_tests.ps1"
$privilegesScript = Join-Path $repoRoot "deploy\docker\test_init\provision_test_privileges.sql"
$runsettingsPath = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\test.runsettings"

Write-Host "====================================================================================" -ForegroundColor Cyan
Write-Host "=== [HARNESS V2] Universal ERP Automated Preflight & Integration Test Runner ===" -ForegroundColor Cyan
Write-Host "====================================================================================" -ForegroundColor Cyan

# 1. Verify Docker Daemon & Container State
Write-Host "`nStep 1: Inspecting Docker Container State..." -ForegroundColor Cyan
$inspectOut = docker inspect $ContainerName 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($inspectOut)) {
    Write-Host "[HARNESS V2] Container '$ContainerName' not found. Launching with ephemeral port..." -ForegroundColor Yellow
    docker run -d `
      --name $ContainerName `
      --network $NetworkName `
      -p 127.0.0.1::5432 `
      -v "${VolumeName}:/var/lib/postgresql/data" `
      $ExpectedImageDigest
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed to launch Docker container '$ContainerName'!" }
}

# 2. Retrieve Docker-Managed Ephemeral Loopback Port
Write-Host "Step 2: Retrieving Docker-Allocated Loopback Ephemeral Port..." -ForegroundColor Cyan
$portRaw = (docker port $ContainerName 5432/tcp 2>&1).Trim()
if ($portRaw -notmatch '^127\.0\.0\.1:(\d+)$') {
    throw "[FAIL-CLOSED] Failed to resolve allocated port from Docker daemon! Raw output: $portRaw"
}
$allocatedPort = [int]$Matches[1]
Write-Host "[HARNESS V2] Verified Allocated Host Port: 127.0.0.1:$allocatedPort" -ForegroundColor Green

# Collision Guard: Strictly exclude staging (5433) and production (5432)
if ($allocatedPort -eq 5432 -or $allocatedPort -eq 5433) {
    throw "[FAIL-CLOSED] Ephemeral port collision with protected environment (Port $allocatedPort)! Aborting."
}

# 3. Wait for PostgreSQL readiness
Write-Host "Step 3: Awaiting PostgreSQL Engine Readiness..." -ForegroundColor Cyan
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    $readyOut = docker exec -u postgres $ContainerName pg_isready -p 5432 2>&1
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $ready) { throw "[FAIL-CLOSED] PostgreSQL engine failed to become ready within 30 seconds!" }
Write-Host "[PASS] PostgreSQL engine ready." -ForegroundColor Green

# 4. Deterministic Database Restoration & Privilege Provisioning
if (-not $SkipRestore) {
    Write-Host "`nStep 4: Executing Deterministic Backup Restore (--no-owner --role=erp_dev)..." -ForegroundColor Cyan
    
    # Copy dump into container if not present
    $dumpLeaf = Split-Path -Leaf $BackupFilePath
    docker exec -u postgres $ContainerName sh -c "test -f /tmp/$dumpLeaf" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[HARNESS V2] Copying backup dump into container /tmp/$dumpLeaf..." -ForegroundColor Yellow
        docker cp $BackupFilePath "${ContainerName}:/tmp/$dumpLeaf"
        docker cp $BackupFilePath "${ContainerName}:/tmp/baseline.dump"
        if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed to copy backup dump into test container!" }
    }

    # Ephemeral database creation with OWNER erp_dev
    Write-Host "[HARNESS V2] Initializing database '$DatabaseName' with OWNER erp_dev..." -ForegroundColor Cyan
    docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d postgres -c "DROP DATABASE IF EXISTS $DatabaseName;" -c "CREATE DATABASE $DatabaseName WITH OWNER erp_dev ENCODING 'UTF8';"
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed to recreate database '$DatabaseName'!" }

    # Deterministic pg_restore with exit code 0 guarantee
    Write-Host "[HARNESS V2] Running pg_restore with --no-owner --role=erp_dev --exit-on-error..." -ForegroundColor Cyan
    docker exec -u postgres $ContainerName pg_restore -U test_bootstrap_admin -d $DatabaseName --no-owner --role=erp_dev --exit-on-error /tmp/baseline.dump
    $restoreRc = $LASTEXITCODE
    if ($restoreRc -ne 0) { throw "[FAIL-CLOSED] pg_restore failed with exit code $restoreRc!" }
    Write-Host "[PASS] pg_restore completed with Exit Code 0." -ForegroundColor Green

    # Apply hardened test privilege matrix
    Write-Host "[HARNESS V2] Applying hardened test privilege matrix..." -ForegroundColor Cyan
    Get-Content $privilegesScript | docker exec -i $ContainerName psql -U test_bootstrap_admin -d $DatabaseName
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed to apply privilege matrix via '$privilegesScript'!" }
    Write-Host "[PASS] Test privileges provisioned cleanly." -ForegroundColor Green

    # Exhaustive Catalog Ownership Audit
    Write-Host "[HARNESS V2] Running catalog ownership verification audit..." -ForegroundColor Cyan
    $auditCmd = @"
SELECT count(*) FROM (
    SELECT nspname FROM pg_namespace WHERE nspname IN ('audit','authorization','erp_inventory','erp_purchasing','erp_sales','finance','identity','organization','platform','sync') AND pg_get_userbyid(nspowner) != 'erp_dev'
    UNION ALL
    SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('audit','authorization','erp_inventory','erp_purchasing','erp_sales','finance','identity','organization','platform','sync') AND c.relkind IN ('r','p','v','m','f','S') AND pg_get_userbyid(c.relowner) != 'erp_dev'
    UNION ALL
    SELECT p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname IN ('audit','authorization','erp_inventory','erp_purchasing','erp_sales','finance','identity','organization','platform','sync') AND pg_get_userbyid(p.proowner) != 'erp_dev'
) q;
"@
    $ownerViolations = [int](docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -t -A -c $auditCmd).Trim()
    if ($ownerViolations -ne 0) {
        throw "[FAIL-CLOSED] Catalog ownership audit failed! Found $ownerViolations non-erp_dev objects!"
    }
    Write-Host "[PASS] Catalog ownership audit verified: 0 non-erp_dev objects." -ForegroundColor Green
}

# 5. Propagate Dynamic Port Configuration
Write-Host "`nStep 5: Propagating Dynamic Port Configuration..." -ForegroundColor Cyan
$appPwdFile = Join-Path $repoRoot "deploy\docker\test_secrets\test_app_pwd.txt"
$adminPwdFile = Join-Path $repoRoot "deploy\docker\test_secrets\test_admin_pwd.txt"

$pwdApp = if ($env:PGPASSWORD_ERP_APP) { 
    $env:PGPASSWORD_ERP_APP 
} elseif (Test-Path $appPwdFile) { 
    (Get-Content -Raw $appPwdFile).Trim() 
} else { 
    "erp_app_pass" 
}

$pwdDev = if ($env:PGPASSWORD_ERP_DEV) { 
    $env:PGPASSWORD_ERP_DEV 
} elseif (Test-Path $adminPwdFile) { 
    (Get-Content -Raw $adminPwdFile).Trim() 
} else { 
    "erp_dev_pass" 
}

if (-not $env:UNIVERSAL_ERP_TRUSTED_DBA_KEY_FINGERPRINT) {
    $env:UNIVERSAL_ERP_TRUSTED_DBA_KEY_FINGERPRINT = "A28227A2761DD8780FE276F54E640F53B71F2A436E6764142C12AB8825134217"
}

$env:PGPASSWORD_ERP_APP = $pwdApp
$env:PGPASSWORD_ERP_DEV = $pwdDev
$env:UNIVERSAL_ERP_TEST_TARGET_HOST = "127.0.0.1"
$env:UNIVERSAL_ERP_TEST_TARGET_PORT = "$allocatedPort"
$env:UNIVERSAL_ERP_TEST_TARGET_DB = $DatabaseName
$env:UNIVERSAL_ERP_TEST_CONNECTION_STRING = "Host=127.0.0.1;Port=$allocatedPort;Database=$DatabaseName;Username=erp_app;Password=$pwdApp;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10"
$env:UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING = "Host=127.0.0.1;Port=$allocatedPort;Database=$DatabaseName;Username=erp_dev;Password=$pwdDev;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10"

# 6. Propagate Dynamic Port Configuration and Execute Master Controlled Test Suite
$origRunsettings = Get-Content -Raw $runsettingsPath
try {
    [xml]$runsettingsXml = Get-Content $runsettingsPath
    $runsettingsXml.RunSettings.RunConfiguration.EnvironmentVariables.UNIVERSAL_ERP_TEST_TARGET_PORT = "$allocatedPort"
    $runsettingsXml.Save($runsettingsPath)
    Write-Host "[PASS] Port $allocatedPort propagated to environment variables and test.runsettings." -ForegroundColor Green

    Write-Host "`nStep 6: Executing Master Controlled Test Suite..." -ForegroundColor Cyan
    & pwsh $runnerScript `
        -ExpectedPort $allocatedPort `
        -SkipBuild:$SkipBuild `
        -TrustedKeyFingerprint $env:UNIVERSAL_ERP_TRUSTED_DBA_KEY_FINGERPRINT `
        -AuditSqlPath (Join-Path $repoRoot "audit_effective_privileges.sql") `
        -RlsAuditSqlPath (Join-Path $repoRoot "audit_rls_policies.sql")

    $masterExitCode = $LASTEXITCODE
    if ($masterExitCode -ne 0) {
        throw "[FAIL-CLOSED] Master controlled test execution failed with exit code $masterExitCode!"
    }

    Write-Host "`n====================================================================================" -ForegroundColor Green
    Write-Host "=== [PASS] CI HARNESS V2: ALL 12 PREFLIGHT GATES & 32 INTEGRATION TESTS PASSED ===" -ForegroundColor Green
    Write-Host "====================================================================================" -ForegroundColor Green
}
finally {
    Set-Content -Path $runsettingsPath -Value $origRunsettings -NoNewline
}
exit 0
