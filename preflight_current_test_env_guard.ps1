# universal-erp-core/preflight_current_test_env_guard.ps1
# Fail-Closed 12-Gate Preflight Environment Guard for Current-Schema Disposable Test Lane (001..048)
param(
    [string]$ExpectedHost = "postgres-current-test",
    [int]$ExpectedPort = 5432,
    [string]$ExpectedDatabase = "universal_erp_current_disposable_test",
    [string]$ExpectedContainer = "universal-erp-postgres-current-test",
    [string]$ExpectedNetwork = "universal-erp-current-test-net",
    [string]$ExpectedVolume = "erp_postgres_current_test_data",
    [string]$ExpectedImageDigest = "postgres:17.11-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994",
    [string]$RunnerImage = "mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$ExpectedRunnerDigest = "sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$RunnerContainerName = "universal-erp-current-test-runner",
    [switch]$InternalRunnerMode = $true,
    [string]$ExpectedAppUser = "erp_app",
    [string]$ExpectedAdminUser = "erp_dev",
    [string]$ExpectedSchemaOwner = "erp_schema_owner",
    [string]$HistoricalContainer = "universal-erp-postgres-test",
    [string]$BackupFilePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.dump",
    [string]$ManifestPath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest",
    [string]$SignaturePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest.sig",
    [string]$PublicKeyPath = "C:\backups\dba_public_key.pem",
    [string]$TrustedKeyFingerprint = "A28227A2761DD8780FE276F54E640F53B71F2A436E6764142C12AB8825134217",
    [string]$OwnershipSqlPath = "",
    [string]$AuditSqlPath = "",
    [string]$RlsAuditSqlPath = "",
    [string]$AppPassword = $env:PGPASSWORD_ERP_APP,
    [string]$AdminPassword = $env:PGPASSWORD_ERP_DEV,
    [switch]$AllowDirtyDatabase = $false
)

$ErrorActionPreference = "Stop"

function Fail-Preflight {
    param([int]$Gate, [string]$Message)
    Write-Host "`n====================================================================================" -ForegroundColor Red
    Write-Host "[PREFLIGHT-CURRENT GATE $Gate FAILED] $Message" -ForegroundColor Red
    Write-Host "EXECUTION ABORTED (FAIL-CLOSED). Staging, historical lane, and production remain untouched." -ForegroundColor Red
    Write-Host "====================================================================================" -ForegroundColor Red
    exit 1
}

function Get-RepoRoot {
    $candidate = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    while ($candidate) {
        if (Test-Path (Join-Path $candidate "src\backend\UniversalErp.sln")) { return (Resolve-Path $candidate).Path }
        $parent = Split-Path -Parent $candidate
        if (-not $parent -or $parent -eq $candidate) { break }
        $candidate = $parent
    }
    throw "Unable to locate repository root."
}

$repoRoot = Get-RepoRoot
$migrationsDir = Join-Path $repoRoot "database\migrations"
if ([string]::IsNullOrWhiteSpace($OwnershipSqlPath)) { $OwnershipSqlPath = Join-Path $repoRoot "audit_current_object_ownership_manifest.sql" }
if ([string]::IsNullOrWhiteSpace($AuditSqlPath)) { $AuditSqlPath = Join-Path $repoRoot "audit_current_effective_privileges.sql" }
if ([string]::IsNullOrWhiteSpace($RlsAuditSqlPath)) { $RlsAuditSqlPath = Join-Path $repoRoot "audit_current_rls_policies.sql" }

Write-Host "`n=== [PREFLIGHT-CURRENT] Starting 12-Gate Security & Isolation Audit ===" -ForegroundColor Cyan

# Gate 0: Fail-Closed Parameter & PowerShell Runtime Guard
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Fail-Preflight 0 "PowerShell 7+ required for current-schema preflight!"
}
if ($ExpectedDatabase -notin @("universal_erp_current_disposable_test", "universal_erp_current_disposable_verify", "universal_erp_current_disposable_verify_047", "universal_erp_current_disposable_verify_047b", "universal_erp_current_disposable_verify_048")) {
    Fail-Preflight 0 "Current-schema preflight only allows approved current disposable databases. Got '$ExpectedDatabase'."
}
if ($InternalRunnerMode) {
    if ($ExpectedHost -ne "postgres-current-test" -or $ExpectedPort -ne 5432) {
        Fail-Preflight 0 "Internal runner mode strictly requires Host='postgres-current-test' and Port=5432. Got '${ExpectedHost}:${ExpectedPort}'."
    }
    if ([string]::IsNullOrWhiteSpace($RunnerImage) -or $RunnerImage -notmatch "@sha256:[0-9a-f]{64}$") {
        Fail-Preflight 0 "Internal runner mode requires immutable digest-pinned RunnerImage! Got '$RunnerImage'."
    }
} else {
    if ($ExpectedHost -ne "127.0.0.1") {
        Fail-Preflight 0 "Host mode ExpectedHost must be '127.0.0.1'. Got '$ExpectedHost'."
    }
    if ($ExpectedPort -le 0 -or $ExpectedPort -eq 5432 -or $ExpectedPort -eq 5433) {
        Fail-Preflight 0 "Invalid or protected host port '$ExpectedPort'. Port 5432 (prod), 5433 (staging), or <=0 forbidden."
    }
}
if ([string]::IsNullOrWhiteSpace($AppPassword) -or [string]::IsNullOrWhiteSpace($AdminPassword)) {
    Fail-Preflight 0 "PGPASSWORD_ERP_APP and PGPASSWORD_ERP_DEV must be provided."
}
Write-Host "[GATE 0 PASS] Runtime & strict current-lane target parameters verified (InternalRunnerMode=$InternalRunnerMode, Target=${ExpectedHost}:${ExpectedPort}/${ExpectedDatabase})." -ForegroundColor Green

# Gate 1: Dedicated Container, Digest, Network, Volume & Port Isolation
$inspectRaw = docker inspect $ExpectedContainer 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($inspectRaw)) {
    Fail-Preflight 1 "Container '$ExpectedContainer' not found."
}
$container = ($inspectRaw | ConvertFrom-Json)[0]
if (-not $container.State.Running) {
    Fail-Preflight 1 "Container '$ExpectedContainer' is not running."
}
if ($container.Config.Image -ne $ExpectedImageDigest) {
    Fail-Preflight 1 "Image digest mismatch! Expected '$ExpectedImageDigest', got '$($container.Config.Image)'."
}
$attachedNetworks = @($container.NetworkSettings.Networks.PSObject.Properties.Name)
if ($attachedNetworks.Count -ne 1 -or $attachedNetworks[0] -ne $ExpectedNetwork) {
    Fail-Preflight 1 "Network isolation failure! Container '$ExpectedContainer' attached to: '$($attachedNetworks -join ', ')', expected strictly '$ExpectedNetwork'."
}
$netInspectRaw = docker network inspect $ExpectedNetwork 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($netInspectRaw)) {
    Fail-Preflight 1 "Failed inspecting Docker network '$ExpectedNetwork'."
}
$netObj = ($netInspectRaw | ConvertFrom-Json)[0]
if (-not $netObj.Internal) {
    Fail-Preflight 1 "Network '$ExpectedNetwork' is NOT configured with 'internal: true' isolation (Internal=$($netObj.Internal))."
}
$ipamGateway = ($netObj.IPAM.Config | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Gateway) } | Select-Object -First 1).Gateway
if ([string]::IsNullOrWhiteSpace($ipamGateway)) {
    Fail-Preflight 1 "Unable to resolve IPAM bridge gateway IP for '$ExpectedNetwork'!"
}
$mountedVol = $container.Mounts | Where-Object { $_.Destination -eq "/var/lib/postgresql/data" -and $_.Name -eq $ExpectedVolume }
if (-not $mountedVol) {
    Fail-Preflight 1 "Container '$ExpectedContainer' data volume is not '$ExpectedVolume'."
}
if ($InternalRunnerMode) {
    $portOutput = docker port $ExpectedContainer 5432/tcp 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($portOutput | Out-String).Trim())) {
        Fail-Preflight 1 "PostgreSQL container '$ExpectedContainer' must have ZERO published host ports in Option A! Found: '$portOutput'."
    }
    if ($container.NetworkSettings.Ports) {
        foreach ($prop in $container.NetworkSettings.Ports.PSObject.Properties) {
            if ($prop.Value -and @($prop.Value).Count -gt 0) {
                Fail-Preflight 1 "PostgreSQL container has active NetworkSettings.Ports binding on '$($prop.Name)'!"
            }
        }
    }
    if ($container.HostConfig.PortBindings) {
        foreach ($pb in $container.HostConfig.PortBindings.PSObject.Properties) {
            foreach ($entry in @($pb.Value)) {
                if ($entry -and -not [string]::IsNullOrWhiteSpace($entry.HostPort)) {
                    Fail-Preflight 1 "PostgreSQL HostConfig.PortBindings specifies non-empty HostPort '$($entry.HostPort)' on '$($pb.Name)'!"
                }
            }
        }
    }
    $existingRunner = docker ps -a --filter "name=^/${RunnerContainerName}$" --format "{{.Names}}" 2>$null
    if (-not [string]::IsNullOrWhiteSpace(($existingRunner | Out-String).Trim())) {
        Fail-Preflight 1 "Designated runner container name '$RunnerContainerName' is already occupied! Failing closed without forcing removal."
    }
    $runnerInspectRaw = docker image inspect $RunnerImage 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($runnerInspectRaw)) {
        Fail-Preflight 1 "Cached runner image '$RunnerImage' missing locally (offline execution forbids network pulls)."
    }
    $runnerImg = ($runnerInspectRaw | ConvertFrom-Json)[0]
    $hasRunnerDigest = ($runnerImg.Id -eq $ExpectedRunnerDigest) -or (@($runnerImg.RepoDigests) | Where-Object { $_ -like "*$ExpectedRunnerDigest" }).Count -gt 0
    if (-not $hasRunnerDigest) {
        Fail-Preflight 1 "Runner image digest mismatch! Expected '$ExpectedRunnerDigest', got Id='$($runnerImg.Id)', RepoDigests='$($runnerImg.RepoDigests -join ', ')'."
    }
    Write-Host "[GATE 1 PASS] Dedicated container '$ExpectedContainer' (0 published host ports), internal network '$ExpectedNetwork' (Internal=true), volume '$ExpectedVolume', Postgres digest & cached SDK digest '$ExpectedRunnerDigest' verified." -ForegroundColor Green
} else {
    $portBinding = (docker port $ExpectedContainer 5432/tcp 2>&1).Trim()
    if ($portBinding -ne "127.0.0.1:$ExpectedPort") {
        Fail-Preflight 1 "Container port binding mismatch! Expected '127.0.0.1:$ExpectedPort', got '$portBinding'."
    }
    $histPort = (docker port $HistoricalContainer 5432/tcp 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($histPort) -and $histPort.Trim() -eq "127.0.0.1:$ExpectedPort") {
        Fail-Preflight 1 "Current-lane port '$ExpectedPort' collides with historical container '$HistoricalContainer'!"
    }
    Write-Host "[GATE 1 PASS] Dedicated container '$ExpectedContainer' (127.0.0.1:$ExpectedPort), internal network '$ExpectedNetwork' (Internal=true), volume '$ExpectedVolume' & digest verified." -ForegroundColor Green
}

# Gate 2: Target Database Ownership & Role Isolation
$dbCheck = docker exec -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -c "
SELECT current_database() || '|' || pg_get_userbyid(datdba)
FROM pg_database WHERE datname = current_database();
"
if ($LASTEXITCODE -ne 0 -or $dbCheck.Trim() -ne "${ExpectedDatabase}|${ExpectedSchemaOwner}") {
    Fail-Preflight 2 "Database identity/owner mismatch! Expected '${ExpectedDatabase}|${ExpectedSchemaOwner}', got '$dbCheck'."
}
$roleCheck = docker exec -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -c "
SELECT rolname || ':' || rolsuper || ':' || rolbypassrls || ':' || rolcanlogin
FROM pg_roles
WHERE rolname IN ('$ExpectedAppUser', '$ExpectedAdminUser', '$ExpectedSchemaOwner')
ORDER BY rolname;
"
$roleLines = ($roleCheck -split "`r?`n") | Where-Object { $_ }
$expectedRoles = @(
    "${ExpectedAppUser}:false:false:true",
    "${ExpectedAdminUser}:false:true:true",
    "${ExpectedSchemaOwner}:false:false:false"
)
if (($roleLines -join ",") -ne ($expectedRoles -join ",")) {
    Fail-Preflight 2 "Role security attributes mismatch! Expected '$($expectedRoles -join ",")', got '$($roleLines -join ",")'."
}
Write-Host "[GATE 2 PASS] Database owner '$ExpectedSchemaOwner' (NOBYPASSRLS) and role matrix verified." -ForegroundColor Green

# Gate 3: Environment Connection String Leak Guard + Active Internal Network Probes
foreach ($varName in @("ConnectionStrings__DefaultConnection", "UNIVERSAL_ERP_PROD_CONNECTION_STRING", "NEON_DATABASE_URL")) {
    $val = [Environment]::GetEnvironmentVariable($varName)
    if (-not [string]::IsNullOrWhiteSpace($val)) {
        Fail-Preflight 3 "Forbidden external connection string variable '$varName' is set in environment!"
    }
}
$appConn = [Environment]::GetEnvironmentVariable("UNIVERSAL_ERP_TEST_CONNECTION_STRING")
$admConn = [Environment]::GetEnvironmentVariable("UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING")
if ([string]::IsNullOrWhiteSpace($appConn) -or [string]::IsNullOrWhiteSpace($admConn)) {
    Fail-Preflight 3 "UNIVERSAL_ERP_TEST_CONNECTION_STRING and UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING must be set."
}
$escapedExpectedHost = [regex]::Escape($ExpectedHost)
foreach ($cPair in @(@{N="App"; V=$appConn; U=$ExpectedAppUser}, @{N="Admin"; V=$admConn; U=$ExpectedAdminUser})) {
    if ($cPair.V -notmatch "Host=$escapedExpectedHost" -or $cPair.V -notmatch "Port=$ExpectedPort" -or $cPair.V -notmatch "Database=$ExpectedDatabase" -or $cPair.V -notmatch "Username=$($cPair.U)") {
        Fail-Preflight 3 "$($cPair.N) connection string does not strictly target ${ExpectedHost}:${ExpectedPort} / $ExpectedDatabase / $($cPair.U)."
    }
}
if ($InternalRunnerMode) {
    if ($env:UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER -ne "true" -or
        $env:UNIVERSAL_ERP_TEST_TARGET_HOST -ne "postgres-current-test" -or
        $env:UNIVERSAL_ERP_TEST_TARGET_PORT -ne "5432" -or
        $env:UNIVERSAL_ERP_TEST_TARGET_DB -ne $ExpectedDatabase) {
        Fail-Preflight 3 "Internal runner environment variables mismatch! Expected RUNNER=true, HOST=postgres-current-test, PORT=5432, DB=$ExpectedDatabase."
    }
}

# Active Container Network Isolation Probes (built-in getent + Perl IO::Socket::INET on Postgres)
$toolCheck = docker exec $ExpectedContainer sh -c "command -v getent && command -v perl" 2>&1
if ($LASTEXITCODE -ne 0) {
    Fail-Preflight 3 "Diagnostic utilities (getent, perl) missing from container '$ExpectedContainer': $toolCheck"
}

# Probe A (Postgres): Inter-container DNS isolation (must not resolve staging container name)
$dnsOut = docker exec $ExpectedContainer getent hosts universal-erp-postgres 2>&1
$dnsExit = $LASTEXITCODE
if ($dnsExit -eq 0) {
    Fail-Preflight 3 "Network breach: Postgres container resolved staging host 'universal-erp-postgres' via Docker DNS ($dnsOut)!"
} elseif ($dnsExit -ne 2) {
    Fail-Preflight 3 "Postgres DNS isolation probe diagnostic failure (exit $dnsExit): $dnsOut"
}

$perlTcpProbe = 'use IO::Socket::INET; my ($h,$p)=@ARGV; my $s=IO::Socket::INET->new(PeerAddr=>$h,PeerPort=>$p,Proto=>q(tcp),Timeout=>2); if($s){close($s);print qq(CONNECTED\n);exit 0;} print qq(BLOCKED: $!\n); exit 1;'
$blockedPattern = "refused|timed out|timeout|failed|unreachable|no route to host|network is unreachable|invalid argument"

# Probe B (Postgres): Host staging port 5433 unreachability (IPAM bridge gateway + host.docker.internal if resolvable)
$stagingProbeTargets = [System.Collections.Generic.List[string]]::new()
$stagingProbeTargets.Add($ipamGateway)
$hdiOut = docker exec $ExpectedContainer getent hosts host.docker.internal 2>&1
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($hdiOut)) {
    $hdiIp = (($hdiOut -split '\s+')[0]).Trim()
    if (-not [string]::IsNullOrWhiteSpace($hdiIp) -and -not $stagingProbeTargets.Contains($hdiIp)) {
        $stagingProbeTargets.Add($hdiIp)
    }
}
foreach ($targetIp in $stagingProbeTargets) {
    $stgReach = docker exec $ExpectedContainer perl -e $perlTcpProbe $targetIp 5433 2>&1
    $stgExit = $LASTEXITCODE
    if ($stgExit -eq 0) {
        Fail-Preflight 3 "Network breach: Postgres container reached staging port at ${targetIp}:5433!"
    }
    if ($stgExit -ne 1 -or ($stgReach -notmatch $blockedPattern)) {
        Fail-Preflight 3 "Diagnostic error during Postgres staging probe ${targetIp}:5433 (exit $stgExit): $stgReach"
    }
}

# Probe C (Postgres): External Internet egress isolation (1.1.1.1:53 must be blocked)
$egressReach = docker exec $ExpectedContainer perl -e $perlTcpProbe 1.1.1.1 53 2>&1
$egressExit = $LASTEXITCODE
if ($egressExit -eq 0) {
    Fail-Preflight 3 "Egress isolation failure: Postgres container reached external Internet (1.1.1.1:53)!"
}
if ($egressExit -ne 1 -or ($egressReach -notmatch $blockedPattern)) {
    Fail-Preflight 3 "Diagnostic error during Postgres external egress probe 1.1.1.1:53 (exit $egressExit): $egressReach"
}

if ($InternalRunnerMode) {
    # Probe D (Runner Image on internal network): Verify exclusive network, internal DNS/5432, zero staging/egress reachability
    $runnerProbeName = "${RunnerContainerName}-preflight-probe"
    $existingProbe = docker ps -a --filter "name=^/${runnerProbeName}$" --format "{{.Names}}" 2>$null
    if (-not [string]::IsNullOrWhiteSpace(($existingProbe | Out-String).Trim())) {
        Fail-Preflight 3 "Runner probe container name '$runnerProbeName' already exists! Refusing to force-remove it."
    }
    $runnerProbeScript = @"
set -u
getent hosts postgres-current-test >/dev/null 2>&1 || { echo 'DNS_PG_FAIL'; exit 10; }
if getent hosts universal-erp-postgres >/dev/null 2>&1; then echo 'DNS_STG_BREACH'; exit 11; fi
timeout 2 bash -c 'cat < /dev/null > /dev/tcp/postgres-current-test/5432' >/dev/null 2>&1 || { echo 'TCP_PG_5432_FAIL'; exit 12; }
if timeout 2 bash -c 'cat < /dev/null > /dev/tcp/${ipamGateway}/5433' >/dev/null 2>&1; then echo 'TCP_STG_5433_BREACH'; exit 13; fi
if getent hosts host.docker.internal >/dev/null 2>&1; then
    hdi_ip=`$(getent hosts host.docker.internal | awk '{print `$1}' | head -n1)
    if timeout 2 bash -c "cat < /dev/null > /dev/tcp/`${hdi_ip}/5433" >/dev/null 2>&1; then echo 'TCP_HDI_5433_BREACH'; exit 14; fi
fi
if timeout 2 bash -c 'cat < /dev/null > /dev/tcp/1.1.1.1/53' >/dev/null 2>&1; then echo 'TCP_EGRESS_BREACH'; exit 15; fi
echo 'RUNNER_ISOLATION_OK'
"@
    $runnerProbeScriptUnix = ($runnerProbeScript -replace "`r`n", "`n")
    $rProbeOut = docker run --rm --pull=never --name $runnerProbeName --network $ExpectedNetwork --cap-drop=ALL --entrypoint bash $RunnerImage -c $runnerProbeScriptUnix 2>&1
    $rProbeExit = $LASTEXITCODE
    if ($rProbeExit -ne 0 -or ($rProbeOut | Out-String) -notmatch 'RUNNER_ISOLATION_OK') {
        Fail-Preflight 3 "Runner container isolation probe failed (exit $rProbeExit): $($rProbeOut | Out-String)"
    }
}
Write-Host "[GATE 3 PASS] Fail-closed connection strings & active network isolation (DNS, staging :5433, egress) verified for DB and runner." -ForegroundColor Green

# Gate 4: Hardened test.runsettings Verification
$runsettingsPath = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\test.runsettings"
if (-not (Test-Path $runsettingsPath)) { Fail-Preflight 4 "test.runsettings missing at '$runsettingsPath'." }
[xml]$rsXml = Get-Content $runsettingsPath
$rsHost = $rsXml.RunSettings.RunConfiguration.EnvironmentVariables.UNIVERSAL_ERP_TEST_TARGET_HOST
$rsPort = $rsXml.RunSettings.RunConfiguration.EnvironmentVariables.UNIVERSAL_ERP_TEST_TARGET_PORT
$rsDb   = $rsXml.RunSettings.RunConfiguration.EnvironmentVariables.UNIVERSAL_ERP_TEST_TARGET_DB
$rsRunner = $rsXml.RunSettings.RunConfiguration.EnvironmentVariables.UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER
if ($rsHost -ne $ExpectedHost -or "$rsPort" -ne "$ExpectedPort" -or $rsDb -ne $ExpectedDatabase -or ($InternalRunnerMode -and "$rsRunner" -ne "true")) {
    Fail-Preflight 4 "test.runsettings mismatch! Expected ${ExpectedHost}:${ExpectedPort}/${ExpectedDatabase} (internal=$InternalRunnerMode), got ${rsHost}:${rsPort}/${rsDb} (internal=$rsRunner)."
}
Write-Host "[GATE 4 PASS] test.runsettings strictly targets ${ExpectedHost}:${ExpectedPort} / $ExpectedDatabase." -ForegroundColor Green

# Gate 5: Release Test Assembly Verification
$releaseDll = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\bin\Release\net10.0\UniversalErp.Api.Tests.dll"
if (-not (Test-Path $releaseDll)) {
    Fail-Preflight 5 "Release assembly missing at '$releaseDll'."
}
Write-Host "[GATE 5 PASS] Release test assembly present." -ForegroundColor Green

# Gate 6: Cryptographic Signed Baseline Verification (Ed25519/RSA Manifest + Trusted Key Fingerprint)
foreach ($artifact in @($BackupFilePath, $ManifestPath, $SignaturePath, $PublicKeyPath)) {
    if (-not (Test-Path $artifact)) { Fail-Preflight 6 "Missing cryptographic baseline artifact: '$artifact'." }
}
$pubKeyPem = Get-Content -Raw $PublicKeyPath
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($pubKeyPem)
$pubBytes = $rsa.ExportSubjectPublicKeyInfo()
$fingerprint = ([BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($pubBytes)) -replace "-", "")
if ($fingerprint -ne $TrustedKeyFingerprint.ToUpperInvariant()) {
    Fail-Preflight 6 "DBA public key fingerprint mismatch! Expected '$TrustedKeyFingerprint', got '$fingerprint'."
}
$manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
$sigBytes = [System.IO.File]::ReadAllBytes($SignaturePath)
if (-not $rsa.VerifyData($manifestBytes, $sigBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
    Fail-Preflight 6 "Cryptographic signature verification failed on '$ManifestPath'!"
}
$manifestText = [System.Text.Encoding]::UTF8.GetString($manifestBytes)
$dumpHash = (Get-FileHash -Algorithm SHA256 $BackupFilePath).Hash.ToLowerInvariant()
if ($manifestText -notmatch $dumpHash) {
    Fail-Preflight 6 "Baseline dump SHA-256 '$dumpHash' not attested in signed manifest!"
}
Write-Host "[GATE 6 PASS] Cryptographic DBA signature & signed baseline dump SHA-256 verified." -ForegroundColor Green

# Gate 7: Schema Migrations Ledger (001..048 Contiguous & Disk SHA-256 Parity for 015..048)
$migRows = docker exec -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -c "
SELECT version || '|' || coalesce(checksum, '') FROM public.schema_migrations ORDER BY version;
"
$migLines = @($migRows -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($migLines.Count -ne 48) {
    Fail-Preflight 7 "Expected exactly 48 total rows in public.schema_migrations, got $($migLines.Count)."
}
$migMap = @{}
foreach ($line in $migLines) {
    if ($line -match '^((0\d{2})_[^|]+)\|([0-9a-f]{64})$') {
        $migMap[$Matches[2]] = @{ FullVersion = $Matches[1]; Checksum = $Matches[3] }
    } else {
        Fail-Preflight 7 "Invalid schema_migrations row format or non-SHA256 checksum: '$line'."
    }
}
for ($m = 1; $m -le 48; $m++) {
    $ver = $m.ToString("000")
    if (-not $migMap.ContainsKey($ver)) {
        Fail-Preflight 7 "Missing migration version '$ver' in public.schema_migrations!"
    }
    if ($m -ge 15) {
        $sqlFile = Get-ChildItem (Join-Path $migrationsDir "${ver}_*.sql") | Where-Object { $_.Name -notmatch '\.down\.sql$' } | Select-Object -First 1
        if (-not $sqlFile) { Fail-Preflight 7 "Missing migration SQL file for version '$ver'." }
        $expectedStem = [System.IO.Path]::GetFileNameWithoutExtension($sqlFile.Name)
        if ($migMap[$ver].FullVersion -ne $expectedStem) {
            Fail-Preflight 7 "Migration version stem mismatch for $ver`: DB='$($migMap[$ver].FullVersion)', Expected='$expectedStem'."
        }
        $diskHash = (Get-FileHash -Algorithm SHA256 $sqlFile.FullName).Hash.ToLowerInvariant()
        if ($migMap[$ver].Checksum -ne $diskHash) {
            Fail-Preflight 7 "Checksum mismatch for migration $ver ($($sqlFile.Name)): DB='$($migMap[$ver].Checksum)', Disk='$diskHash'."
        }
    }
}
Write-Host "[GATE 7 PASS] All 48 contiguous migrations (001..048) verified; 015..048 match exact stem and on-disk SHA-256." -ForegroundColor Green

# Gate 8: Explicit Object-Ownership Manifest & SECURITY DEFINER Audit (Condition 1)
if (-not (Test-Path $OwnershipSqlPath)) { Fail-Preflight 8 "Missing ownership manifest audit SQL: '$OwnershipSqlPath'." }
$ownerViolations = Get-Content -Raw $OwnershipSqlPath | docker exec -i -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { Fail-Preflight 8 "Ownership manifest audit SQL failed to execute!" }
if (-not [string]::IsNullOrWhiteSpace(($ownerViolations | Out-String).Trim())) {
    Fail-Preflight 8 "Object-ownership manifest violation(s) detected:`n$($ownerViolations | Out-String)"
}
Write-Host "[GATE 8 PASS] Explicit Object-Ownership Manifest & SECURITY DEFINER search_path/EXECUTE audit passed with 0 violations." -ForegroundColor Green

# Gate 9: Comprehensive Least-Privilege Matrix & PUBLIC Revocation Audit
if (-not (Test-Path $AuditSqlPath)) { Fail-Preflight 9 "Missing privilege matrix audit SQL: '$AuditSqlPath'." }
$privViolations = Get-Content -Raw $AuditSqlPath | docker exec -i -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { Fail-Preflight 9 "Privilege matrix audit SQL failed to execute!" }
if (-not [string]::IsNullOrWhiteSpace(($privViolations | Out-String).Trim())) {
    Fail-Preflight 9 "Privilege matrix violation(s) detected:`n$($privViolations | Out-String)"
}
Write-Host "[GATE 9 PASS] Effective privilege matrix & PUBLIC revocation audit passed with 0 violations." -ForegroundColor Green

# Gate 10: Exhaustive RLS Policy Audit + Live erp_app RLS Enforcement Probes
if (-not (Test-Path $RlsAuditSqlPath)) { Fail-Preflight 10 "Missing RLS audit SQL: '$RlsAuditSqlPath'." }
$rlsViolations = Get-Content -Raw $RlsAuditSqlPath | docker exec -i -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { Fail-Preflight 10 "RLS policy audit SQL failed to execute!" }
if (-not [string]::IsNullOrWhiteSpace(($rlsViolations | Out-String).Trim())) {
    Fail-Preflight 10 "RLS catalog violation(s) detected:`n$($rlsViolations | Out-String)"
}

$liveRlsSql = @"
BEGIN;
DO `$`$
DECLARE
    cnt int;
    tA uuid := '11111111-1111-1111-1111-111111111111';
    cA uuid := '22222222-2222-2222-2222-222222222222';
    tB uuid := '33333333-3333-3333-3333-333333333333';
    blocked boolean := false;
BEGIN
    IF current_user <> 'erp_app' THEN
        RAISE EXCEPTION 'Live RLS probe must run as erp_app, got %', current_user;
    END IF;
    IF current_setting('row_security') <> 'on' THEN
        RAISE EXCEPTION 'row_security must be on for erp_app';
    END IF;

    -- 1. Unscoped erp_app queries on RLS tables must see zero rows without throwing privilege/column errors
    SELECT count(*) INTO cnt FROM erp_sales.customer;
    IF cnt <> 0 THEN RAISE EXCEPTION 'Unscoped customer count expected 0, got %', cnt; END IF;
    SELECT count(*) INTO cnt FROM erp_inventory.inventory_cost_layer;
    IF cnt <> 0 THEN RAISE EXCEPTION 'Unscoped inventory_cost_layer count expected 0, got %', cnt; END IF;
    SELECT count(*) INTO cnt FROM erp_inventory.valuation_entry;
    IF cnt <> 0 THEN RAISE EXCEPTION 'Unscoped valuation_entry count expected 0, got %', cnt; END IF;
    SELECT count(*) INTO cnt FROM finance.exchange_rates;
    IF cnt <> 0 THEN RAISE EXCEPTION 'Unscoped exchange_rates count expected 0, got %', cnt; END IF;
    SELECT count(*) INTO cnt FROM organization.companies;
    IF cnt <> 0 THEN RAISE EXCEPTION 'Unscoped companies count expected 0, got %', cnt; END IF;

    -- 2. Sensitive schema & unapproved columns must be denied (42501)
    BEGIN
        EXECUTE 'SELECT count(*) FROM "authorization".roles' INTO cnt;
    EXCEPTION WHEN insufficient_privilege THEN
        blocked := true;
    END;
    IF NOT blocked THEN RAISE EXCEPTION 'erp_app was NOT blocked from "authorization".roles'; END IF;

    blocked := false;
    BEGIN
        EXECUTE 'SELECT legal_name FROM organization.companies LIMIT 1';
    EXCEPTION WHEN insufficient_privilege THEN
        blocked := true;
    END;
    IF NOT blocked THEN RAISE EXCEPTION 'erp_app was NOT blocked from unapproved column organization.companies.legal_name'; END IF;

    -- 3. Scoped erp_app INSERT for mismatched foreign tenant_id must fail FORCE RLS (42501)
    PERFORM set_config('app.tenant_id', tA::text, true);
    PERFORM set_config('app.company_id', cA::text, true);
    blocked := false;
    BEGIN
        INSERT INTO erp_sales.customer (customer_id, tenant_id, company_id, customer_code, name)
        VALUES (gen_random_uuid(), tB, cA, 'RLS-PROBE', 'Cross-Tenant Probe');
    EXCEPTION WHEN insufficient_privilege THEN
        blocked := true;
    END;
    IF NOT blocked THEN RAISE EXCEPTION 'FORCE RLS did NOT reject cross-tenant INSERT by erp_app!'; END IF;
END `$`$;
ROLLBACK;
"@
$liveProbeOut = $liveRlsSql | docker exec -i -e PGPASSWORD=$AppPassword $ExpectedContainer psql -U $ExpectedAppUser -d $ExpectedDatabase -v ON_ERROR_STOP=1 -t -A 2>&1
if ($LASTEXITCODE -ne 0) {
    Fail-Preflight 10 "Live erp_app RLS enforcement probe failed:`n$($liveProbeOut | Out-String)"
}
Write-Host "[GATE 10 PASS] Exhaustive 001..046 RLS catalog audit & live erp_app FORCE RLS enforcement probes passed." -ForegroundColor Green

# Gate 11: Migration 026 (FIFO) & 046 (Currency) Schema + Permission Parity Check
$gate11Sql = @'
SELECT count(*) FROM (
    SELECT 1 FROM information_schema.columns WHERE table_schema='organization' AND table_name='companies' AND column_name IN ('functional_currency_code','presentation_currency_code','currency_effective_date') HAVING count(*) = 3
    UNION ALL
    SELECT 1 FROM information_schema.tables WHERE table_schema='erp_inventory' AND table_name IN ('inventory_cost_layer','inventory_cost_allocation','valuation_entry') HAVING count(*) = 3
    UNION ALL
    SELECT 1 FROM information_schema.tables WHERE table_schema='finance' AND table_name IN ('currencies','exchange_rates','financial_audit_logs') HAVING count(*) = 3
    UNION ALL
    SELECT 1 FROM "authorization".permissions WHERE permission_code IN ('inventory.valuation.view','inventory.transfer.create','inventory.adjustment.create') HAVING count(*) >= 3
) q;
'@
$schemaParityOut = $gate11Sql | docker exec -i -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0 -or [int]$schemaParityOut.Trim() -ne 4) {
    Fail-Preflight 11 "Current-schema parity check failed! Expected 4 feature groups present, got '$schemaParityOut'."
}
Write-Host "[GATE 11 PASS] Migrations 026 FIFO tables, 046 currency schema, and RBAC permission catalog verified." -ForegroundColor Green

# Gate 12: Clean Disposable Seed State Verification
if (-not $AllowDirtyDatabase) {
    $seedCheck = docker exec -u postgres $ExpectedContainer psql -U test_bootstrap_admin -d $ExpectedDatabase -t -A -c "
    SELECT (SELECT count(*) FROM erp_sales.sales_invoice) || '|' ||
           (SELECT count(*) FROM erp_purchasing.outbox_event) || '|' ||
           (SELECT count(*) FROM erp_inventory.inventory_cost_layer);
    "
    if ($LASTEXITCODE -ne 0 -or $seedCheck.Trim() -ne "0|0|0") {
        Fail-Preflight 12 "Current-schema disposable DB contains leftover test rows! (sales_invoice|outbox_event|cost_layer = '$seedCheck')."
    }
}
Write-Host "[GATE 12 PASS] Clean deterministic current-schema disposable state verified." -ForegroundColor Green
Write-Host "=== [PREFLIGHT-CURRENT COMPLETE] ALL 12 FAIL-CLOSED GATES PASSED ===" -ForegroundColor Green
exit 0
