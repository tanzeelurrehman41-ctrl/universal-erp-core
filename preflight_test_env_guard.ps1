# preflight_test_env_guard.ps1
# Hardened 12-Gate Preflight Test Environment Guard
param(
    [string]$ExpectedHost = "127.0.0.1",
    [int]$ExpectedPort = 5434,
    [string]$ExpectedDatabase = "universal_erp_disposable_test",
    [string]$ExpectedContainer = "universal-erp-postgres-test",
    [string]$ExpectedImageDigest = "postgres:17.11-bookworm@sha256:4a3dfb128540b616ec485d2da8a96495db6ea0fa4f8f4803ea6ff910c2ec857c",
    [string]$ExpectedNetwork = "universal-erp-test-net",
    [string]$ExpectedVolume = "erp_postgres_test_data",
    [string]$ExpectedAppUser = "erp_app",
    [string]$ExpectedAdminUser = "erp_dev",
    [string]$BackupFilePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.dump",
    [string]$ManifestPath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest",
    [string]$SignaturePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest.sig",
    [string]$PublicKeyPath = "C:\backups\dba_public_key.pem",
    [string]$TrustedKeyFingerprint = $env:UNIVERSAL_ERP_TRUSTED_DBA_KEY_FINGERPRINT,
    [string]$AuditSqlPath = "C:\backups\audit_effective_privileges.sql",
    [string]$RlsAuditSqlPath = "C:\backups\audit_rls_policies.sql"
)

$ErrorActionPreference = "Stop"

# Enforce PowerShell 7+ or .NET 10 Runtime Compatibility
$psMajor = $PSVersionTable.PSVersion.Major
$psMinor = $PSVersionTable.PSVersion.Minor
$isCompatiblePs = ($psMajor -ge 7)

if (-not $isCompatiblePs) {
    Write-Host @"
====================================================================================
[FAIL-CLOSED] INCOMPATIBLE POWERSHELL RUNTIME DETECTED!
Current Shell: PowerShell $($PSVersionTable.PSVersion)
Current .NET Runtime: $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)
Required Shell: PowerShell 7+ (pwsh.exe, built on .NET Core/.NET 10)
Reason: UniversalErp.Api.Tests targets net10.0. Loading net10.0 assemblies (Npgsql.dll)
        requires the .NET host runtime provided by PowerShell 7+.
====================================================================================
"@ -ForegroundColor Red
    exit 1
}

Write-Host "PowerShell Runtime Verified: PowerShell $($PSVersionTable.PSVersion) on $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)" -ForegroundColor Green

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
Write-Host "=== [PREFLIGHT] Validating Dedicated Test Environment (12-Gate Verification) ===" -ForegroundColor Cyan

# Gate 0: Require Connection Strings
$appConnStr = $env:UNIVERSAL_ERP_TEST_CONNECTION_STRING
if ([string]::IsNullOrWhiteSpace($appConnStr)) { Write-Host "[FAIL-CLOSED] Gate 0: UNIVERSAL_ERP_TEST_CONNECTION_STRING missing!" -ForegroundColor Red; exit 1 }
$adminConnStr = $env:UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING
if ([string]::IsNullOrWhiteSpace($adminConnStr)) { Write-Host "[FAIL-CLOSED] Gate 0: UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING missing!" -ForegroundColor Red; exit 1 }

# Gate 2: Container Running & Exact Image Digest
Write-Host "Gate 2: Verifying container identity and exact image digest..." -ForegroundColor Cyan
$inspectJson = docker inspect $ExpectedContainer 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($inspectJson)) {
    Write-Host "[FAIL-CLOSED] Gate 2: Container '$ExpectedContainer' not found or docker inspect failed!" -ForegroundColor Red
    exit 1
}
$cData = $inspectJson | ConvertFrom-Json
if ($cData.Count -gt 0) { $cData = $cData[0] }

if ($cData.State.Status -ne "running") { Write-Host "[FAIL-CLOSED] Gate 2: Container is not running!" -ForegroundColor Red; exit 1 }

# Verify actual immutable image identity via docker image inspect against RepoDigests
$imgInspectJson = docker image inspect $cData.Image 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($imgInspectJson)) {
    Write-Host "[FAIL-CLOSED] Gate 2: Unable to inspect container image ID '$($cData.Image)'!" -ForegroundColor Red
    exit 1
}
$imgData = ($imgInspectJson | ConvertFrom-Json)
if ($imgData.Count -gt 0) { $imgData = $imgData[0] }
$expectedSha = ($ExpectedImageDigest.Split('@')[-1]).Trim()
$hasDigest = $false
foreach ($rd in $imgData.RepoDigests) {
    if ($rd -like "*$expectedSha*") { $hasDigest = $true; break }
}
if (-not $hasDigest -and $cData.Image -ne $expectedSha) {
    Write-Host "[FAIL-CLOSED] Gate 2: Immutable image digest mismatch! Expected image to match digest '$expectedSha', got RepoDigests: $($imgData.RepoDigests -join ', ')." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 2: Container running and image digest verified." -ForegroundColor Green

# Gate 3: Fail-Closed Network Isolation, Staging Unreachability & Egress Control
Write-Host "Gate 3: Verifying network isolation and staging unreachability..." -ForegroundColor Cyan
$attachedNetworks = @($cData.NetworkSettings.Networks.PSObject.Properties.Name)
if ($attachedNetworks.Count -ne 1 -or $attachedNetworks[0] -ne $ExpectedNetwork) {
    Write-Host "[FAIL-CLOSED] Gate 3: Network isolation failure! Attached: $($attachedNetworks -join ', '), expected strictly '$ExpectedNetwork'." -ForegroundColor Red
    exit 1
}

# Verify diagnostic tools exist before probing
$toolCheck = docker exec $ExpectedContainer sh -c "command -v getent && command -v nc" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL-CLOSED] Gate 3: Diagnostic utilities (getent, nc) missing from test container: $toolCheck" -ForegroundColor Red
    exit 1
}

# A. Inter-container DNS isolation check: test container must NOT resolve staging container name
$dnsOut = docker exec $ExpectedContainer getent hosts universal-erp-postgres 2>&1
$dnsExit = $LASTEXITCODE
if ($dnsExit -eq 0) {
    Write-Host "[FAIL-CLOSED] Gate 3: Network breach: test container successfully resolved staging container 'universal-erp-postgres' via Docker DNS!" -ForegroundColor Red
    exit 1
} elseif ($dnsExit -ne 2) {
    Write-Host "[FAIL-CLOSED] Gate 3: getent probe failed with diagnostic/execution error (exit code $dnsExit): $dnsOut" -ForegroundColor Red
    exit 1
}

# B. Host gateway staging reachability check: test container must NOT reach host port 5433
$gatewayIp = $cData.NetworkSettings.Networks."$ExpectedNetwork".Gateway
if ([string]::IsNullOrWhiteSpace($gatewayIp)) {
    Write-Host "[FAIL-CLOSED] Gate 3: Unable to determine network gateway IP for '$ExpectedNetwork'!" -ForegroundColor Red
    exit 1
}
$stagingReach = docker exec $ExpectedContainer nc -z -v -w 2 $gatewayIp 5433 2>&1
$stagingExit = $LASTEXITCODE
if ($stagingExit -eq 0) {
    Write-Host "[FAIL-CLOSED] Gate 3: Network breach: test container reached staging host port at ${gatewayIp}:5433!" -ForegroundColor Red
    exit 1
}
$isRefusedOrTimeout = ($stagingReach -match "refused|timed out|timeout|failed")
if ($stagingExit -ne 1 -or -not $isRefusedOrTimeout) {
    Write-Host "[FAIL-CLOSED] Gate 3: Diagnostic error during staging probe (exit code $stagingExit): $stagingReach" -ForegroundColor Red
    exit 1
}

# C. External Internet egress check: runtime canary probe
$egressProbe = docker exec $ExpectedContainer nc -z -v -w 2 1.1.1.1 53 2>&1
$egressExit = $LASTEXITCODE
if ($egressExit -eq 0) {
    Write-Host "[FAIL-CLOSED] Gate 3: Egress isolation failure: container successfully connected to external Internet (1.1.1.1:53)!" -ForegroundColor Red
    exit 1
}
$isEgressBlocked = ($egressProbe -match "refused|timed out|timeout|failed")
if ($egressExit -ne 1 -or -not $isEgressBlocked) {
    Write-Host "[FAIL-CLOSED] Gate 3: Diagnostic error during external egress probe (exit code $egressExit): $egressProbe" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 3: Inter-container DNS, host staging port (${gatewayIp}:5433), and external egress isolation verified." -ForegroundColor Green

# Gate 4: Dedicated Storage Volume & Non-Shared Assertion (All Containers Including Stopped)
Write-Host "Gate 4: Verifying dedicated storage volume and non-sharing status across all containers..." -ForegroundColor Cyan
$hasDedicatedVolume = $false
foreach ($m in $cData.Mounts) {
    if ($m.Destination -eq "/var/lib/postgresql/data") {
        if ($m.Type -eq "volume" -and $m.Name -eq $ExpectedVolume) { $hasDedicatedVolume = $true }
        else { Write-Host "[FAIL-CLOSED] Gate 4: Storage failure: mount is $($m.Type) '$($m.Name)', expected volume '$ExpectedVolume'!" -ForegroundColor Red; exit 1 }
    }
}
if (-not $hasDedicatedVolume) { Write-Host "[FAIL-CLOSED] Gate 4: Volume '$ExpectedVolume' is not mounted!" -ForegroundColor Red; exit 1 }

$allContainers = (docker ps -a -q)
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL-CLOSED] Gate 4: Failed to enumerate containers via 'docker ps -a -q'! Exit code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
if ($allContainers) {
    foreach ($cid in $allContainers) {
        if ($cid -ne $cData.Id) {
            $mountInspect = docker inspect --format '{{range .Mounts}}{{.Name}} {{end}}' $cid 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[FAIL-CLOSED] Gate 4: Failed to inspect mounts for container '$cid'! Error: $mountInspect" -ForegroundColor Red
                exit 1
            }
            $otherMounts = $mountInspect.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
            if ($ExpectedVolume -in $otherMounts) {
                Write-Host "[FAIL-CLOSED] Gate 4: Storage breach: dedicated volume '$ExpectedVolume' is shared with container '$cid'!" -ForegroundColor Red
                exit 1
            }
        }
    }
}
Write-Host "[PASS] Gate 4: Dedicated non-shared volume mount verified." -ForegroundColor Green

# Gate 5: Loopback-Only Port Binding
Write-Host "Gate 5: Verifying loopback-only port binding..." -ForegroundColor Cyan
$portBindings = $cData.NetworkSettings.Ports."5432/tcp"
if (-not $portBindings -or $portBindings.Count -ne 1 -or $portBindings[0].HostIp -ne $ExpectedHost -or [int]$portBindings[0].HostPort -ne $ExpectedPort) {
    Write-Host "[FAIL-CLOSED] Gate 5: Host port binding failure! Expected strictly ${ExpectedHost}:${ExpectedPort} -> 5432/tcp." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 5: Loopback-only port binding (${ExpectedHost}:${ExpectedPort}) verified." -ForegroundColor Green

# Gate 6: Parse Connection Strings & Target Database Identity
Write-Host "Gate 6: Verifying endpoint targets..." -ForegroundColor Cyan
$npgsqlDllPath = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\bin\Release\net10.0\Npgsql.dll"
if (-not (Test-Path $npgsqlDllPath)) { Write-Host "[FAIL-CLOSED] Gate 6: Npgsql.dll not found at '$npgsqlDllPath'!" -ForegroundColor Red; exit 1 }
Add-Type -Path $npgsqlDllPath

$appBuilder = New-Object Npgsql.NpgsqlConnectionStringBuilder($appConnStr)
$adminBuilder = New-Object Npgsql.NpgsqlConnectionStringBuilder($adminConnStr)

if ($appBuilder.Database -ne $ExpectedDatabase -or $adminBuilder.Database -ne $ExpectedDatabase) {
    Write-Host "[FAIL-CLOSED] Gate 6: Target database mismatch! Both endpoints must connect to '$ExpectedDatabase'." -ForegroundColor Red
    exit 1
}
if ($appBuilder.Host -ne $ExpectedHost -or [int]$appBuilder.Port -ne $ExpectedPort -or
    $adminBuilder.Host -ne $ExpectedHost -or [int]$adminBuilder.Port -ne $ExpectedPort) {
    Write-Host "[FAIL-CLOSED] Gate 6: Endpoint host/port mismatch! Expected '${ExpectedHost}:${ExpectedPort}'." -ForegroundColor Red
    exit 1
}
if ($appBuilder.Username -ne $ExpectedAppUser -or $adminBuilder.Username -ne $ExpectedAdminUser) {
    Write-Host "[FAIL-CLOSED] Gate 6: Endpoint user mismatch! Expected App='$ExpectedAppUser', Admin='$ExpectedAdminUser'." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 6: Endpoint targets ($ExpectedAppUser / $ExpectedAdminUser -> ${ExpectedHost}:${ExpectedPort}/$ExpectedDatabase) verified." -ForegroundColor Green

# Query Live Database Attributes for Gates 1, 7, 8, 9
function Query-EndpointAttributes($builder, $label) {
    $conn = New-Object Npgsql.NpgsqlConnection($builder.ConnectionString)
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = @"
            SELECT 
                current_database(), session_user::text, current_user::text,
                r.rolsuper, r.rolbypassrls, r.rolcanlogin, r.rolreplication,
                (SELECT system_identifier::text FROM pg_control_system()),
                EXTRACT(EPOCH FROM pg_postmaster_start_time())::numeric(16,6)::text,
                pg_is_in_recovery()::text,
                coalesce(inet_server_addr()::text, 'local'),
                coalesce(inet_server_port()::text, 'local'),
                version()
            FROM pg_roles r
            WHERE r.rolname = current_user;
"@
        $reader = $cmd.ExecuteReader()
        if (-not $reader.Read()) { throw "Failed to read attributes from $label endpoint." }
        $attrs = [PSCustomObject]@{
            Database            = $reader.GetString(0)
            SessionUser         = $reader.GetString(1)
            CurrentUser         = $reader.GetString(2)
            IsSuper             = $reader.GetBoolean(3)
            BypassRls           = $reader.GetBoolean(4)
            CanLogin            = $reader.GetBoolean(5)
            Replication         = $reader.GetBoolean(6)
            SystemIdentifier    = $reader.GetString(7)
            PostmasterStartEpoch= $reader.GetString(8)
            IsInRecovery        = $reader.GetString(9)
            ServerAddr          = $reader.GetString(10)
            ServerPort          = $reader.GetString(11)
            Version             = $reader.GetString(12)
        }
        $reader.Close()
        return $attrs
    }
    finally {
        if ($conn.State -eq [System.Data.ConnectionState]::Open) { $conn.Close() }
    }
}

$appAttrs = Query-EndpointAttributes $appBuilder "Application"
$adminAttrs = Query-EndpointAttributes $adminBuilder "Admin"

# Gate 1: Exact Version 17.11 & Target Database Identity
if ($appAttrs.Version -notmatch "^PostgreSQL 17\.11\b" -or $adminAttrs.Version -notmatch "^PostgreSQL 17\.11\b") {
    Write-Host "[FAIL-CLOSED] Gate 1: Exact PostgreSQL 17.11 required! Got: '$($appAttrs.Version)'." -ForegroundColor Red
    exit 1
}
if ($appAttrs.Database -ne $ExpectedDatabase -or $adminAttrs.Database -ne $ExpectedDatabase) {
    Write-Host "[FAIL-CLOSED] Gate 1: Live database identity violation! Expected '$ExpectedDatabase', got app='$($appAttrs.Database)', admin='$($adminAttrs.Database)'." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 1: Exact PostgreSQL 17.11 and live database '$ExpectedDatabase' verified on both endpoints." -ForegroundColor Green

# Gate 7: Operational Cluster Parity
if ($appAttrs.SystemIdentifier -ne $adminAttrs.SystemIdentifier -or
    $appAttrs.PostmasterStartEpoch -ne $adminAttrs.PostmasterStartEpoch -or
    $appAttrs.IsInRecovery -ne "False" -or $adminAttrs.IsInRecovery -ne "False") {
    Write-Host "[FAIL-CLOSED] Gate 7: Operational cluster parity failure! Endpoints connect to different daemons." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 7: Cluster parity (system_identifier and postmaster start time match) verified." -ForegroundColor Green

# Gate 8: erp_app Role Attributes
if ($appAttrs.SessionUser -ne $ExpectedAppUser -or $appAttrs.CurrentUser -ne $ExpectedAppUser -or
    $appAttrs.IsSuper -eq $true -or $appAttrs.BypassRls -eq $true -or $appAttrs.CanLogin -eq $false -or $appAttrs.Replication -eq $true) {
    Write-Host "[FAIL-CLOSED] Gate 8: erp_app identity violation! SessionUser=$($appAttrs.SessionUser), CurrentUser=$($appAttrs.CurrentUser), Super=$($appAttrs.IsSuper), BypassRLS=$($appAttrs.BypassRls), Replication=$($appAttrs.Replication)." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 8: erp_app identity and RLS enforcement (NOBYPASSRLS) verified." -ForegroundColor Green

# Gate 9: erp_dev Role Attributes
if ($adminAttrs.SessionUser -ne $ExpectedAdminUser -or $adminAttrs.CurrentUser -ne $ExpectedAdminUser -or
    $adminAttrs.IsSuper -eq $true -or $adminAttrs.BypassRls -eq $false -or $adminAttrs.CanLogin -eq $false -or $adminAttrs.Replication -eq $true) {
    Write-Host "[FAIL-CLOSED] Gate 9: erp_dev identity violation! SessionUser=$($adminAttrs.SessionUser), CurrentUser=$($adminAttrs.CurrentUser), Super=$($adminAttrs.IsSuper), BypassRLS=$($adminAttrs.BypassRls), Replication=$($adminAttrs.Replication)." -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Gate 9: erp_dev identity and fixture privilege (BYPASSRLS) verified." -ForegroundColor Green

# Gate 9.1: Post-Restoration Migration Ledger State Invariant (Exact 14-Version Set Verification)
Write-Host "Gate 9.1: Verifying exact migration ledger state in public.schema_migrations..." -ForegroundColor Cyan
$expectedVersions = @(
    "001_platform_foundation",
    "002_inventory",
    "003_local_dev_bootstrap",
    "004_offline_sync_foundation",
    "005_oidc_identity_and_permissions",
    "006_oidc_workspace_lookup",
    "007_inventory_workflow_permissions",
    "008_sync_change_feed",
    "009_sync_change_feed_index",
    "010_offline_device_enrollment",
    "011_purchasing",
    "012_saas_tenant_onboarding",
    "013_double_entry_accounting",
    "014_finance_chart_of_accounts_seed"
)

$connLedger = New-Object Npgsql.NpgsqlConnection($adminBuilder.ConnectionString)
try {
    $connLedger.Open()
    $cmdLedger = $connLedger.CreateCommand()
    $cmdLedger.CommandText = "SELECT version FROM public.schema_migrations ORDER BY version ASC;"
    $readerLedger = $cmdLedger.ExecuteReader()
    $actualVersions = New-Object System.Collections.Generic.List[string]
    while ($readerLedger.Read()) {
        $actualVersions.Add($readerLedger.GetString(0))
    }
    $readerLedger.Close()

    # 1. Exact total count
    if ($actualVersions.Count -ne 14) {
        Write-Host "[FAIL-CLOSED] Gate 9.1: Total ledger version count ($($actualVersions.Count)) is not exactly 14!" -ForegroundColor Red
        exit 1
    }

    # 2. Check for unauthorized migrations (015-019 range)
    $unauthorized = $actualVersions | Where-Object { $_ -match "^(015|016|017|018|019)\b" }
    if ($unauthorized) {
        Write-Host "[FAIL-CLOSED] Gate 9.1: Unauthorized migrations detected in ledger: $($unauthorized -join ', ')!" -ForegroundColor Red
        exit 1
    }

    # 3. Check for duplicates
    $duplicates = $actualVersions | Group-Object | Where-Object { $_.Count -gt 1 }
    if ($duplicates) {
        Write-Host "[FAIL-CLOSED] Gate 9.1: Duplicate migration entries detected in ledger: $($duplicates.Name -join ', ')!" -ForegroundColor Red
        exit 1
    }

    # 4. Check for missing required baseline versions
    $missing = $expectedVersions | Where-Object { $_ -notin $actualVersions }
    if ($missing) {
        Write-Host "[FAIL-CLOSED] Gate 9.1: Missing required baseline migrations in ledger: $($missing -join ', ')!" -ForegroundColor Red
        exit 1
    }

    # 5. Check for unexpected versions
    $unexpected = $actualVersions | Where-Object { $_ -notin $expectedVersions }
    if ($unexpected) {
        Write-Host "[FAIL-CLOSED] Gate 9.1: Unexpected migration entries detected in ledger: $($unexpected -join ', ')!" -ForegroundColor Red
        exit 1
    }

    Write-Host "[PASS] Gate 9.1: Exact 14 baseline migration versions verified (0 missing, 0 unexpected, 0 duplicates, 0 unauthorized 015-019)." -ForegroundColor Green
}
finally {
    if ($connLedger.State -eq [System.Data.ConnectionState]::Open) { $connLedger.Close() }
}

# Gate 10: Cryptographically Verified Backup Provenance
Write-Host "Gate 10: Verifying cryptographic provenance of backup artifact and manifest..." -ForegroundColor Cyan
if (-not (Test-Path $BackupFilePath) -or -not (Test-Path $ManifestPath) -or -not (Test-Path $SignaturePath) -or -not (Test-Path $PublicKeyPath)) {
    Write-Host "[FAIL-CLOSED] Gate 10: Backup dump, manifest, signature, or public key file missing!" -ForegroundColor Red
    exit 1
}
if ([string]::IsNullOrWhiteSpace($TrustedKeyFingerprint)) {
    Write-Host "[FAIL-CLOSED] Gate 10: Out-of-band trusted DBA key fingerprint not supplied!" -ForegroundColor Red
    exit 1
}

# Verify public key fingerprint
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem((Get-Content -LiteralPath $PublicKeyPath -Raw))
$sha = [System.Security.Cryptography.SHA256]::Create()
$pubDer = $rsa.ExportSubjectPublicKeyInfo()
$pubFp = [BitConverter]::ToString($sha.ComputeHash($pubDer)).Replace("-", "").ToUpperInvariant()
$normExpected = $TrustedKeyFingerprint.Trim().Replace(":", "").Replace("-", "").ToUpperInvariant()
if ($pubFp -ne $normExpected) {
    Write-Host "[FAIL-CLOSED] Gate 10: Public key fingerprint mismatch! Expected: $TrustedKeyFingerprint, Got: $pubFp" -ForegroundColor Red
    exit 1
}

# Verify RSA signature of manifest
$manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
$signatureBytes = [System.IO.File]::ReadAllBytes($SignaturePath)
$sigValid = $rsa.VerifyData($manifestBytes, $signatureBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
if (-not $sigValid) {
    Write-Host "[FAIL-CLOSED] Gate 10: Digital signature verification failed for DBA manifest!" -ForegroundColor Red
    exit 1
}

# Parse manifest with strict schema and value validation
function Parse-StrictManifest($filePath) {
    $allowedKeys = @("ARTIFACT_NAME", "FILE_SIZE_BYTES", "SHA256", "APPROVED_BY", "APPROVAL_DATE", "SCHEMA_INVENTORY", "SEED_FIXTURES", "SANITIZATION_ASSERTION")
    $dict = @{}
    $lines = Get-Content $filePath
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) { continue }
        $colonIdx = $trimmed.IndexOf(":")
        if ($colonIdx -lt 1) { throw "Malformed manifest line: '$trimmed'" }
        $k = $trimmed.Substring(0, $colonIdx).Trim()
        $v = $trimmed.Substring($colonIdx + 1).Trim()
        if ($k -notin $allowedKeys) { throw "Unexpected key in manifest: '$k'" }
        if ($dict.ContainsKey($k)) { throw "Duplicate key in manifest: '$k'" }
        if ([string]::IsNullOrWhiteSpace($v)) { throw "Empty value for mandatory key: '$k'" }
        $dict[$k] = $v
    }
    foreach ($ak in $allowedKeys) {
        if (-not $dict.ContainsKey($ak)) { throw "Missing mandatory key in manifest: '$ak'" }
    }

    # Strict Manifest Value Validations
    if ($dict["ARTIFACT_NAME"] -ne "universal_erp_m001_m019_baseline_sanitized.dump") {
        throw "Invalid ARTIFACT_NAME: '$($dict["ARTIFACT_NAME"])'. Expected: 'universal_erp_m001_m019_baseline_sanitized.dump'"
    }

    $sizeVal = 0
    if (-not [int64]::TryParse($dict["FILE_SIZE_BYTES"], [ref]$sizeVal) -or $sizeVal -le 0) {
        throw "Invalid FILE_SIZE_BYTES value: '$($dict["FILE_SIZE_BYTES"])'. Must be a positive integer greater than 0."
    }

    if ($dict["SHA256"] -notmatch "^[a-fA-F0-9]{64}$") {
        throw "Invalid SHA256 format: '$($dict["SHA256"])'. Must be exactly 64 hexadecimal characters."
    }
    if ($dict["SHA256"].ToLowerInvariant() -eq "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") {
        throw "Invalid SHA256: Hash corresponds to an empty string (0-byte file)."
    }

    if ($dict["APPROVED_BY"] -match "<.*>" -or $dict["APPROVED_BY"].Trim().Length -lt 3) {
        throw "Invalid APPROVED_BY value: '$($dict["APPROVED_BY"])'. Must identify the actual certifying DBA."
    }

    $approvalDate = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($dict["APPROVAL_DATE"], [ref]$approvalDate)) {
        throw "Invalid APPROVAL_DATE format: '$($dict["APPROVAL_DATE"])'. Must be a valid ISO 8601 timestamp."
    }
    if ($approvalDate -gt [DateTimeOffset]::UtcNow.AddHours(1)) {
        throw "Invalid APPROVAL_DATE: Timestamp is in the future: '$($dict["APPROVAL_DATE"])'."
    }
    if ($approvalDate -lt [DateTimeOffset]::Parse("2026-01-01T00:00:00Z")) {
        throw "Invalid APPROVAL_DATE: Timestamp precedes valid project baseline: '$($dict["APPROVAL_DATE"])'."
    }

    $expectedSchemas = @("audit", "authorization", "erp_inventory", "erp_purchasing", "erp_sales", "finance", "identity", "organization", "platform", "sync")
    $actualSchemas = $dict["SCHEMA_INVENTORY"].Split(",") | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -ne "" } | Sort-Object
    if (($actualSchemas -join ",") -ne ($expectedSchemas -join ",")) {
        throw "Invalid SCHEMA_INVENTORY: Does not match approved 10-schema inventory. Expected: $($expectedSchemas -join ', '), got: $($actualSchemas -join ', ')"
    }

    if ($dict["SEED_FIXTURES"] -ne "certified_sanitized_seed_v1") {
        throw "Invalid SEED_FIXTURES value: '$($dict["SEED_FIXTURES"])'. Expected: 'certified_sanitized_seed_v1'."
    }

    if ($dict["SANITIZATION_ASSERTION"] -ne "zero_pii_zero_live_credentials") {
        throw "Invalid SANITIZATION_ASSERTION value: '$($dict["SANITIZATION_ASSERTION"])'. Expected: 'zero_pii_zero_live_credentials'."
    }

    return $dict
}

try {
    $manifestDict = Parse-StrictManifest $ManifestPath
} catch {
    Write-Host "[FAIL-CLOSED] Gate 10: Manifest parsing or strict value validation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$backupItem = Get-Item $BackupFilePath
if ($backupItem.Name -ne $manifestDict["ARTIFACT_NAME"] -or $backupItem.Length -ne [int64]$manifestDict["FILE_SIZE_BYTES"]) {
    Write-Host "[FAIL-CLOSED] Gate 10: Backup artifact name or size does not match verified manifest!" -ForegroundColor Red
    exit 1
}
$actualHash = (Get-FileHash -Path $BackupFilePath -Algorithm SHA256).Hash.ToUpper()
if ($actualHash -ne $manifestDict["SHA256"].ToUpper()) {
    Write-Host "[FAIL-CLOSED] Gate 10: Backup artifact SHA-256 hash mismatch!" -ForegroundColor Red
    exit 1
}

# PostgreSQL Custom Format Magic Header Verification (PGDMP)
$headerStream = [System.IO.File]::OpenRead($BackupFilePath)
try {
    $headerBytes = New-Object byte[] 5
    $readCount = $headerStream.Read($headerBytes, 0, 5)
    $expectedMagic = [byte[]]@(0x50, 0x47, 0x44, 0x4D, 0x50) # 'P','G','D','M','P'
    if ($readCount -ne 5) {
        Write-Host "[FAIL-CLOSED] Gate 10: Backup artifact too short to contain PostgreSQL custom archive header!" -ForegroundColor Red
        exit 1
    }
    for ($i = 0; $i -lt 5; $i++) {
        if ($headerBytes[$i] -ne $expectedMagic[$i]) {
            Write-Host "[FAIL-CLOSED] Gate 10: Invalid archive format! First 5 bytes do not match 'PGDMP' magic header." -ForegroundColor Red
            exit 1
        }
    }
} finally {
    $headerStream.Dispose()
}

# Mandatory PostgreSQL Archive Readability Verification (Fail-Closed)
$pgRestoreCmd = Get-Command pg_restore -ErrorAction SilentlyContinue
if (-not $pgRestoreCmd) {
    Write-Host "[FAIL-CLOSED] Gate 10: Mandatory prerequisite failed: 'pg_restore' CLI is not installed or not in PATH! Archive verification cannot proceed." -ForegroundColor Red
    exit 1
}
$tocOutput = & pg_restore -l $BackupFilePath 2>&1
$tocRc = $LASTEXITCODE
if ($tocRc -ne 0) {
    Write-Host "[FAIL-CLOSED] Gate 10: Archive TOC verification failed! 'pg_restore -l' returned exit code $tocRc." -ForegroundColor Red
    exit 1
}
$tocEntryCount = ($tocOutput | Where-Object { $_ -notmatch '^;' -and $_.Trim() -ne '' }).Count
Write-Host "[PASS] Gate 10: Archive TOC verified readable via pg_restore ($tocEntryCount catalog entries enumerated)." -ForegroundColor Green

Write-Host "[PASS] Gate 10: Cryptographic backup artifact, manifest provenance, magic header, and archive TOC verified." -ForegroundColor Green

# Gate 11: Dedicated RLS Configuration & Policy Audit
Write-Host "Gate 11: Executing Row Level Security (RLS) configuration & policy audit..." -ForegroundColor Cyan
if (-not (Test-Path $RlsAuditSqlPath)) { Write-Host "[FAIL-CLOSED] Gate 11: RLS Audit SQL script not found at '$RlsAuditSqlPath'!" -ForegroundColor Red; exit 1 }
$rlsSql = Get-Content $RlsAuditSqlPath -Raw

function Invoke-AuditQuery($connString, $sqlScript) {
    $conn = New-Object Npgsql.NpgsqlConnection($connString)
    $violations = New-Object System.Collections.Generic.List[PSCustomObject]
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $sqlScript
        $reader = $cmd.ExecuteReader()
        while ($reader.Read()) {
            $violations.Add([PSCustomObject]@{
                Category   = $reader.GetString(0)
                Object     = $reader.GetString(1)
                Attribute  = $reader.GetString(2)
                Expected   = $reader.GetString(3)
                Actual     = $reader.GetString(4)
                Detail     = $reader.GetString(5)
            })
        }
        $reader.Close()
        return $violations
    }
    finally {
        if ($conn.State -eq [System.Data.ConnectionState]::Open) { $conn.Close() }
    }
}

$rlsViolations = Invoke-AuditQuery $adminConnStr $rlsSql
if ($rlsViolations.Count -gt 0) {
    Write-Host "`n[FAIL-CLOSED] GATE 11: RLS AUDIT FAILED ($($rlsViolations.Count) violations)!" -ForegroundColor Red
    foreach ($v in $rlsViolations) {
        Write-Host "[$($v.Category)] $($v.Object) | Attr: $($v.Attribute) | $($v.Expected) | $($v.Actual) | $($v.Detail)" -ForegroundColor Red
    }
    exit 1
}
Write-Host "[PASS] Gate 11: Row Level Security policies verified." -ForegroundColor Green

# Gate 12: Exhaustive Effective Privilege Catalog Audit
Write-Host "Gate 12: Executing exhaustive effective privilege catalog audit..." -ForegroundColor Cyan
if (-not (Test-Path $AuditSqlPath)) { Write-Host "[FAIL-CLOSED] Gate 12: Audit SQL script not found at '$AuditSqlPath'!" -ForegroundColor Red; exit 1 }
$auditSql = Get-Content $AuditSqlPath -Raw

$violations = Invoke-AuditQuery $appConnStr $auditSql
if ($violations.Count -gt 0) {
    Write-Host "`n[FAIL-CLOSED] GATE 12: EFFECTIVE PRIVILEGE AUDIT FAILED ($($violations.Count) violations)!" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "[$($v.Category)] $($v.Object) | Priv: $($v.Attribute) | $($v.Expected) | $($v.Actual) | $($v.Detail)" -ForegroundColor Red
    }
    exit 1
}
Write-Host "[PASS] Gate 12: Effective privileges verified." -ForegroundColor Green

Write-Host "[PASS] All 12 preflight isolation and security gates successfully verified." -ForegroundColor Green
exit 0
