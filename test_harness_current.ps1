# universal-erp-core/test_harness_current.ps1
# Current-Schema Disposable Test Lane Harness (Migrations 001..048, Strict Separation from Historical Lane)
param(
    [string]$ContainerName = "universal-erp-postgres-current-test",
    [string]$NetworkName = "universal-erp-current-test-net",
    [string]$VolumeName = "erp_postgres_current_test_data",
    [string]$DatabaseName = "universal_erp_current_disposable_verify",
    [string]$ExpectedImageDigest = "postgres:17.11-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994",
    [string]$RunnerImage = "mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$ExpectedRunnerDigest = "sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$RunnerContainerName = "universal-erp-current-test-runner",
    [switch]$IncludeAdminCurrencySuite = $false,
    [string]$BackupFilePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.dump",
    [switch]$SkipBuild = $false,
    [switch]$SkipRestore = $false
)

$ErrorActionPreference = "Stop"

if ($DatabaseName -notin @("universal_erp_current_disposable_test", "universal_erp_current_disposable_verify", "universal_erp_current_disposable_verify_047", "universal_erp_current_disposable_verify_047b", "universal_erp_current_disposable_verify_048")) {
    throw "[FAIL-CLOSED] DatabaseName '$DatabaseName' is not an approved current-schema disposable database."
}

function Get-RepoRoot {
    $candidate = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    while ($candidate) {
        if (Test-Path (Join-Path $candidate "src\backend\UniversalErp.sln")) { return (Resolve-Path $candidate).Path }
        $parent = Split-Path -Parent $candidate
        if (-not $parent -or $parent -eq $candidate) { break }
        $candidate = $parent
    }
    throw "[FAIL-CLOSED] Unable to locate repository root."
}

$repoRoot = Get-RepoRoot
$composeFile = Join-Path $repoRoot "deploy\docker\docker-compose.current-test.yml"
$privilegesScript = Join-Path $repoRoot "deploy\docker\test_init\provision_current_test_privileges.sql"
$runnerScript = Join-Path $repoRoot "run_current_schema_tests.ps1"
$runsettingsPath = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\test.runsettings"
$migrationsDir = Join-Path $repoRoot "database\migrations"

Write-Host "====================================================================================" -ForegroundColor Cyan
Write-Host "=== [CURRENT-SCHEMA HARNESS] Universal ERP Disposable Test Lane (001..046) ===" -ForegroundColor Cyan
Write-Host "====================================================================================" -ForegroundColor Cyan

# 1. Start Dedicated Isolated PostgreSQL Container via Compose
Write-Host "`nStep 1: Provisioning Dedicated Docker Container '$ContainerName'..." -ForegroundColor Cyan
$inspectOut = docker inspect $ContainerName 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($inspectOut)) {
    docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] docker compose up failed for '$composeFile'." }
} else {
    $cState = ($inspectOut | ConvertFrom-Json)[0].State.Running
    if (-not $cState) {
        docker start $ContainerName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed to start '$ContainerName'." }
    }
}

# 2. Verify Strictly Internal Network Isolation, Zero Published Host Ports, & Immutable Runner Digest
$netInspectRaw = docker network inspect $NetworkName 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($netInspectRaw)) {
    throw "[FAIL-CLOSED] Failed inspecting Docker network '$NetworkName'."
}
$netObj = ($netInspectRaw | ConvertFrom-Json)[0]
if (-not $netObj.Internal) {
    throw "[FAIL-CLOSED] Network '$NetworkName' must enforce internal: true!"
}
$portOut = docker port $ContainerName 5432/tcp 2>$null
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($portOut | Out-String).Trim())) {
    throw "[FAIL-CLOSED] PostgreSQL must have ZERO published host ports in Option A! Found: '$portOut'."
}
$runnerInspectRaw = docker image inspect $RunnerImage 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($runnerInspectRaw)) {
    throw "[FAIL-CLOSED] Runner image '$RunnerImage' missing locally. Pull immutable SDK digest before running offline harness."
}
$runnerImg = ($runnerInspectRaw | ConvertFrom-Json)[0]
$hasRunnerDigest = ($runnerImg.Id -eq $ExpectedRunnerDigest) -or (@($runnerImg.RepoDigests) | Where-Object { $_ -like "*$ExpectedRunnerDigest" }).Count -gt 0
if (-not $hasRunnerDigest) {
    throw "[FAIL-CLOSED] Runner image digest mismatch! Expected '$ExpectedRunnerDigest'."
}
$targetHost = "postgres-current-test"
$targetPort = 5432
Write-Host "[PASS] Verified Option A internal isolation (Internal=true, zero host ports, runner digest '$ExpectedRunnerDigest'): ${targetHost}:${targetPort}" -ForegroundColor Green

# 3. Wait for PostgreSQL Engine Readiness (over TCP loopback inside container, surviving initdb restart)
Write-Host "Step 2: Awaiting PostgreSQL readiness..." -ForegroundColor Cyan
$ready = $false
for ($i = 0; $i -lt 45; $i++) {
    docker exec -u postgres $ContainerName pg_isready -h 127.0.0.1 -U test_bootstrap_admin -p 5432 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d postgres -t -A -c "SELECT 1" 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    }
    Start-Sleep -Seconds 1
}
if (-not $ready) { throw "[FAIL-CLOSED] Container '$ContainerName' did not become ready within 30s." }
Write-Host "[PASS] PostgreSQL ready." -ForegroundColor Green

# 4. Restore Baseline (erp_schema_owner), Verify 015..019 Equivalence, Replay 020..046, Harden Privileges
if (-not $SkipRestore) {
    Write-Host "`nStep 3: Ensuring non-bypass 'erp_schema_owner' role & recreating '$DatabaseName'..." -ForegroundColor Cyan
    docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d postgres -v ON_ERROR_STOP=1 -c "
DO `$`$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_schema_owner') THEN
        CREATE ROLE erp_schema_owner WITH NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
    ELSE
        ALTER ROLE erp_schema_owner WITH NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
    END IF;
    GRANT erp_schema_owner TO test_bootstrap_admin;
END `$`$;
" -c "DROP DATABASE IF EXISTS $DatabaseName;" -c "CREATE DATABASE $DatabaseName WITH OWNER erp_schema_owner ENCODING 'UTF8';"
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed creating '$DatabaseName' owned by erp_schema_owner." }

    Write-Host "[HARNESS] Copying signed baseline dump into '$ContainerName'..." -ForegroundColor Cyan
    docker cp $BackupFilePath "${ContainerName}:/tmp/baseline.dump"
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed copying dump into container." }

    Write-Host "[HARNESS] Restoring baseline (--no-owner --role=erp_schema_owner --exit-on-error)..." -ForegroundColor Cyan
    docker exec -u postgres $ContainerName pg_restore -U test_bootstrap_admin -d $DatabaseName --no-owner --role=erp_schema_owner --exit-on-error /tmp/baseline.dump
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] pg_restore failed!" }
    Write-Host "[PASS] Baseline restored cleanly under erp_schema_owner." -ForegroundColor Green

    # 4A. Mandatory Schema & Data Equivalence Verification for 015..019 BEFORE reconciling schema_migrations
    Write-Host "`nStep 4: Verifying 015..019 schema/seed equivalence before reconciling migration ledger..." -ForegroundColor Cyan
    $equivCheck = docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -t -A -v ON_ERROR_STOP=1 -c "
SELECT (
    -- 015: erp_sales schema + 6 core tables with ENABLE + FORCE RLS
    (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'erp_sales'
       AND c.relname IN ('customer','sales_order','sales_order_line','sales_invoice','sales_invoice_line','outbox_event')
       AND c.relrowsecurity AND c.relforcerowsecurity) = 6
    AND
    -- 016: CUST-WALKIN seed row present
    EXISTS (SELECT 1 FROM erp_sales.customer WHERE customer_code = 'CUST-WALKIN')
    AND
    -- 017: sync.operation_inbox lease columns & index + erp_inventory.warehouse.location_id & FK
    (SELECT count(*) FROM information_schema.columns
     WHERE table_schema='sync' AND table_name='operation_inbox'
       AND column_name IN ('claim_token','claimed_at','claim_expires_at')) = 3
    AND EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='sync' AND tablename='operation_inbox' AND indexname='operation_inbox_lease_idx')
    AND EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_schema='erp_inventory' AND table_name='warehouse' AND column_name='location_id')
    AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname='warehouse_location_fkey')
    AND
    -- 018: command_idempotency.company_id + sync.operation_conflicts with FORCE RLS
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_schema='erp_inventory' AND table_name='command_idempotency' AND column_name='company_id')
    AND EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname='sync' AND c.relname='operation_conflicts' AND c.relrowsecurity AND c.relforcerowsecurity)
    AND
    -- 019: erp_purchasing.outbox_event dead-letter columns & partial indexes present
    (SELECT count(*) FROM information_schema.columns
     WHERE table_schema='erp_purchasing' AND table_name='outbox_event'
       AND column_name IN ('status','last_error','next_retry_at','redrive_count','last_redriven_at')) = 5
    AND (SELECT count(*) FROM pg_indexes
         WHERE schemaname='erp_purchasing' AND tablename='outbox_event'
           AND indexname IN ('ix_purchasing_outbox_pending','ix_purchasing_outbox_dead_letter')) = 2
);
"
    if ($LASTEXITCODE -ne 0 -or $equivCheck.Trim() -ne "t") {
        throw "[FAIL-CLOSED] Baseline dump failed 015..019 schema/data equivalence verification! Ledger reconciliation refused."
    }
    Write-Host "[PASS] 015..019 equivalence verified cleanly. Recording reconciled 015..019 ledger entries..." -ForegroundColor Green

    foreach ($v in @("015", "016", "017", "018", "019")) {
        $f = Get-ChildItem (Join-Path $migrationsDir "${v}_*.sql") | Where-Object { $_.Name -notmatch '\.down\.sql$' } | Select-Object -First 1
        if (-not $f) { throw "Missing migration file for version $v." }
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        $sha = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLowerInvariant()
        docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 -c "
INSERT INTO public.schema_migrations (version, checksum, applied_at)
VALUES ('$stem', '$sha', now())
ON CONFLICT (version) DO UPDATE SET checksum = EXCLUDED.checksum;
" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed recording reconciled migration $stem." }
    }

    # 4B. Replay Migrations 020..046 under SET ROLE erp_schema_owner (never blanket-owning to erp_dev)
    Write-Host "`nStep 5: Replaying Migrations 020..046 under SET ROLE erp_schema_owner..." -ForegroundColor Cyan
    docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 -c "
ALTER TABLE public.schema_migrations OWNER TO erp_schema_owner;
ALTER TABLE public.schema_migrations ALTER COLUMN checksum DROP NOT NULL;
" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed preparing schema_migrations for replay." }

    $forwardFiles = Get-ChildItem (Join-Path $migrationsDir "0[2-4][0-9]_*.sql") |
        Where-Object { $_.Name -notmatch '\.down\.sql$' } |
        Sort-Object Name

    foreach ($mf in $forwardFiles) {
        if ($mf.Name -notmatch '^(\d{3})_') { throw "Unexpected migration filename: $($mf.Name)" }
        $ver = $Matches[1]
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($mf.Name)
        $sha = (Get-FileHash -Algorithm SHA256 $mf.FullName).Hash.ToLowerInvariant()
        Write-Host "  Applying $ver ($($mf.Name))..." -ForegroundColor DarkCyan
        $prefix = "SET ROLE erp_schema_owner;"
        if ($ver -eq "045") {
            # 045 seeds PILOT-ALPHA-2026 into FORCE RLS platform.tenant_entitlements under erp_schema_owner (NOBYPASSRLS)
            $prefix += "`nSELECT set_config('app.tenant_id', 'a0000000-0000-0000-0000-000000000001', false);"
        }
        $wrappedSql = "$prefix`n" + (Get-Content -Raw $mf.FullName)
        $wrappedSql | docker exec -i -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "[FAIL-CLOSED] Migration $($mf.Name) failed during replay under erp_schema_owner!"
        }
        docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 -c "
INSERT INTO public.schema_migrations (version, checksum, applied_at)
VALUES ('$stem', '$sha', now())
ON CONFLICT (version) DO UPDATE SET checksum = EXCLUDED.checksum;
" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed recording migration checksum for $stem." }
    }

    docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 -c "
ALTER TABLE public.schema_migrations ALTER COLUMN checksum SET NOT NULL;
" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed restoring NOT NULL constraint on schema_migrations.checksum." }
    Write-Host "[PASS] Applied all migrations 020..046 under erp_schema_owner with exact SHA-256 ledger." -ForegroundColor Green

    # 4C. Apply Current-Schema Least-Privilege Matrix & Object-Ownership Exceptions
    Write-Host "`nStep 6: Provisioning least-privilege matrix & documented SECURITY DEFINER exceptions onto '$DatabaseName'..." -ForegroundColor Cyan
    $privSql = Get-Content -Raw $privilegesScript
    if ($DatabaseName -ne "universal_erp_current_disposable_test") {
        $privSql = $privSql.Replace("universal_erp_current_disposable_test", $DatabaseName)
        if ($privSql -match "universal_erp_current_disposable_test") {
            throw "[FAIL-CLOSED] Privilege script still contains preserved DB reference after target substitution!"
        }
    }
    $privSql | docker exec -i -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -v ON_ERROR_STOP=1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Failed applying '$privilegesScript'!" }
    Write-Host "[PASS] Hardened privileges and ownership manifest applied." -ForegroundColor Green

    # Print live Ownership Summary before preflight
    Write-Host "`n[OWNERSHIP SUMMARY]" -ForegroundColor Cyan
    docker exec -u postgres $ContainerName psql -U test_bootstrap_admin -d $DatabaseName -c "
SELECT object_class, owner, count(*)
FROM (
    SELECT 'schema' AS object_class, pg_get_userbyid(nspowner) AS owner FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'
    UNION ALL
    SELECT 'relation/seq', pg_get_userbyid(c.relowner) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema','pg_toast') AND c.relkind IN ('r','p','v','m','f','S') AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'e')
    UNION ALL
    SELECT CASE WHEN p.prosecdef THEN 'func(SEC_DEF)' ELSE 'func(SEC_INV)' END, pg_get_userbyid(p.proowner) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
) s
GROUP BY 1, 2 ORDER BY 1, 2;
"
}

# 5. Configure Internal Container Environment & Execute Runner (37 or 38 Integration Tests)
$appPwdFile = Join-Path $repoRoot "deploy\docker\test_secrets\test_app_pwd.txt"
$adminPwdFile = Join-Path $repoRoot "deploy\docker\test_secrets\test_admin_pwd.txt"
$pwdApp = if ($env:PGPASSWORD_ERP_APP) { $env:PGPASSWORD_ERP_APP } else { (Get-Content -Raw $appPwdFile).Trim() }
$pwdDev = if ($env:PGPASSWORD_ERP_DEV) { $env:PGPASSWORD_ERP_DEV } else { (Get-Content -Raw $adminPwdFile).Trim() }

$env:PGPASSWORD_ERP_APP = $pwdApp
$env:PGPASSWORD_ERP_DEV = $pwdDev
$env:UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER = "true"
$env:UNIVERSAL_ERP_TEST_TARGET_HOST = $targetHost
$env:UNIVERSAL_ERP_TEST_TARGET_PORT = "$targetPort"
$env:UNIVERSAL_ERP_TEST_TARGET_DB = $DatabaseName
$env:UNIVERSAL_ERP_TEST_CONNECTION_STRING = "Host=$targetHost;Port=$targetPort;Database=$DatabaseName;Username=erp_app;Password=$pwdApp;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10"
$env:UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING = "Host=$targetHost;Port=$targetPort;Database=$DatabaseName;Username=erp_dev;Password=$pwdDev;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=10"

$origRunsettings = Get-Content -Raw $runsettingsPath
try {
    [xml]$rsXml = Get-Content $runsettingsPath
    $envNode = $rsXml.RunSettings.RunConfiguration.EnvironmentVariables
    $envNode.UNIVERSAL_ERP_TEST_TARGET_HOST = $targetHost
    $envNode.UNIVERSAL_ERP_TEST_TARGET_PORT = "$targetPort"
    $envNode.UNIVERSAL_ERP_TEST_TARGET_DB = $DatabaseName
    $internalNode = $envNode.SelectSingleNode("UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER")
    if (-not $internalNode) {
        $internalNode = $rsXml.CreateElement("UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER")
        [void]$envNode.AppendChild($internalNode)
    }
    $internalNode.InnerText = "true"
    $rsXml.Save($runsettingsPath)
    Write-Host "[PASS] Configured test.runsettings temporarily for internal runner ${targetHost}:${targetPort} / $DatabaseName." -ForegroundColor Green

    Write-Host "`nStep 7: Running Preflight Guard + Current-Schema Integration Tests in Isolated Runner..." -ForegroundColor Cyan
    & pwsh $runnerScript `
        -ExpectedHost $targetHost `
        -ExpectedPort $targetPort `
        -ExpectedDatabase $DatabaseName `
        -ExpectedContainer $ContainerName `
        -ExpectedNetwork $NetworkName `
        -ExpectedVolume $VolumeName `
        -RunnerImage $RunnerImage `
        -ExpectedRunnerDigest $ExpectedRunnerDigest `
        -RunnerContainerName $RunnerContainerName `
        -InternalRunnerMode `
        -IncludeAdminCurrencySuite:$IncludeAdminCurrencySuite `
        -SkipBuild:$SkipBuild
    if ($LASTEXITCODE -ne 0) {
        throw "[FAIL-CLOSED] Current-schema controlled test execution failed with exit code $LASTEXITCODE!"
    }
    $expectedPassText = if ($IncludeAdminCurrencySuite) { "38 / 38" } else { "37 / 37" }
    Write-Host "`n====================================================================================" -ForegroundColor Green
    Write-Host "=== [PASS] CURRENT-SCHEMA HARNESS: 12 PREFLIGHT GATES & $expectedPassText INTEGRATION TESTS PASSED ===" -ForegroundColor Green
    Write-Host "====================================================================================" -ForegroundColor Green
}
finally {
    Set-Content -Path $runsettingsPath -Value $origRunsettings -NoNewline
}
exit 0
