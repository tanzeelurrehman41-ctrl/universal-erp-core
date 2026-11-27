# universal-erp-core/run_current_schema_tests.ps1
# Master Current-Schema Controlled Test Runner (All 37 Tests across 3 Suites; Migrations 001..046)
param(
    [string]$ExpectedHost = "postgres-current-test",
    [int]$ExpectedPort = 5432,
    [string]$ExpectedDatabase = "universal_erp_current_disposable_test",
    [string]$ExpectedContainer = "universal-erp-postgres-current-test",
    [string]$ExpectedNetwork = "universal-erp-current-test-net",
    [string]$ExpectedVolume = "erp_postgres_current_test_data",
    [string]$RunnerImage = "mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$ExpectedRunnerDigest = "sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d",
    [string]$RunnerContainerName = "universal-erp-current-test-runner",
    [switch]$InternalRunnerMode = $true,
    [switch]$AllowDirtyDatabase = $false,
    [switch]$IncludeAdminCurrencySuite = $false,
    [string]$ExpectedAppUser = "erp_app",
    [string]$ExpectedAdminUser = "erp_dev",
    [string]$ExpectedSchemaOwner = "erp_schema_owner",
    [string]$BackupFilePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.dump",
    [string]$ManifestPath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest",
    [string]$SignaturePath = "C:\backups\universal_erp_m001_m019_baseline_sanitized.manifest.sig",
    [string]$PublicKeyPath = "C:\backups\dba_public_key.pem",
    [string]$TrustedKeyFingerprint = "A28227A2761DD8780FE276F54E640F53B71F2A436E6764142C12AB8825134217",
    [string]$OwnershipSqlPath = "",
    [string]$AuditSqlPath = "",
    [string]$RlsAuditSqlPath = "",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "[FAIL-CLOSED] PowerShell 7+ (pwsh) required for net10.0 Npgsql test execution." -ForegroundColor Red
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
    throw "[FAIL-CLOSED] Unable to locate repository root."
}

$repoRoot = Get-RepoRoot
$solutionPath = Join-Path $repoRoot "src\backend\UniversalErp.sln"
$testProject = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\UniversalErp.Api.Tests.csproj"
$runsettings = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\test.runsettings"
$resultsDir = Join-Path $repoRoot "test-results-current"
$releaseAssembly = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\bin\Release\net10.0\UniversalErp.Api.Tests.dll"
$preflightScript = Join-Path $repoRoot "preflight_current_test_env_guard.ps1"

if ([string]::IsNullOrWhiteSpace($OwnershipSqlPath)) { $OwnershipSqlPath = Join-Path $repoRoot "audit_current_object_ownership_manifest.sql" }
if ([string]::IsNullOrWhiteSpace($AuditSqlPath)) { $AuditSqlPath = Join-Path $repoRoot "audit_current_effective_privileges.sql" }
if ([string]::IsNullOrWhiteSpace($RlsAuditSqlPath)) { $RlsAuditSqlPath = Join-Path $repoRoot "audit_current_rls_policies.sql" }

try {
    # 1. Release Compilation (Explicit Non-Incremental)
    if (-not $SkipBuild) {
        Write-Host "`n=== [BUILD] Compiling Solution in Release Mode (Non-Incremental) ===" -ForegroundColor Cyan
        $buildStartUtc = [DateTime]::UtcNow.AddSeconds(-2)
        dotnet build $solutionPath --configuration Release --no-incremental --nologo
        if ($LASTEXITCODE -ne 0) { throw "'dotnet build -c Release --no-incremental' failed with exit code $LASTEXITCODE." }
        if (-not (Test-Path $releaseAssembly) -or (Get-Item $releaseAssembly).LastWriteTimeUtc -lt $buildStartUtc) {
            throw "Release test assembly missing or stale at '$releaseAssembly'."
        }
        Write-Host "[PASS] Release build completed." -ForegroundColor Green
    } else {
        if (-not (Test-Path $releaseAssembly)) { throw "SkipBuild specified but Release assembly missing at '$releaseAssembly'." }
        Write-Host "[INFO] SkipBuild requested; verified Release assembly '$releaseAssembly'." -ForegroundColor Yellow
    }

    # 2. Run Fail-Closed 12-Gate Preflight Guard in Isolated Child Process
    Write-Host "`n=== [PREFLIGHT-CURRENT] Invoking 12-Gate Preflight Guard ===" -ForegroundColor Cyan
    $preflightArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", "`"$preflightScript`"",
        "-ExpectedHost", $ExpectedHost,
        "-ExpectedPort", $ExpectedPort,
        "-ExpectedDatabase", $ExpectedDatabase,
        "-ExpectedContainer", $ExpectedContainer,
        "-ExpectedNetwork", $ExpectedNetwork,
        "-ExpectedVolume", $ExpectedVolume,
        "-RunnerImage", $RunnerImage,
        "-ExpectedRunnerDigest", $ExpectedRunnerDigest,
        "-RunnerContainerName", $RunnerContainerName,
        "-ExpectedAppUser", $ExpectedAppUser,
        "-ExpectedAdminUser", $ExpectedAdminUser,
        "-ExpectedSchemaOwner", $ExpectedSchemaOwner,
        "-BackupFilePath", "`"$BackupFilePath`"",
        "-ManifestPath", "`"$ManifestPath`"",
        "-SignaturePath", "`"$SignaturePath`"",
        "-PublicKeyPath", "`"$PublicKeyPath`"",
        "-TrustedKeyFingerprint", $TrustedKeyFingerprint,
        "-OwnershipSqlPath", "`"$OwnershipSqlPath`"",
        "-AuditSqlPath", "`"$AuditSqlPath`"",
        "-RlsAuditSqlPath", "`"$RlsAuditSqlPath`""
    )
    if ($InternalRunnerMode) { $preflightArgs += "-InternalRunnerMode" } else { $preflightArgs += "-InternalRunnerMode:`$false" }
    if ($AllowDirtyDatabase) { $preflightArgs += "-AllowDirtyDatabase" }
    $preflightProc = Start-Process -FilePath "pwsh.exe" -ArgumentList $preflightArgs -NoNewWindow -Wait -PassThru
    if ($preflightProc.ExitCode -ne 0) {
        throw "Current-schema preflight guard failed with exit code $($preflightProc.ExitCode). Test execution FORBIDDEN."
    }

    # 3. Suite Runner (NEVER aborts early — always executes all suites and records exact TRX counts)
    function Invoke-CurrentSuite {
        param(
            [Parameter(Mandatory=$true)][string]$Filter,
            [Parameter(Mandatory=$true)][string]$SuiteName,
            [Parameter(Mandatory=$true)][int]$ExpectedExactTests
        )

        $runGuid = [Guid]::NewGuid().ToString("D")
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $trxFileName = "${SuiteName}_${timestamp}_${runGuid}.trx"
        $trxFullPath = Join-Path $resultsDir $trxFileName
        if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null }

        Write-Host "`n>>> [RUNNING CURRENT-SCHEMA SUITE] $SuiteName | Expected: $ExpectedExactTests | Filter: $Filter" -ForegroundColor Cyan
        if ($InternalRunnerMode) {
            $existingRunner = docker ps -a --filter "name=^/${RunnerContainerName}$" --format "{{.Names}}" 2>$null
            if (-not [string]::IsNullOrWhiteSpace(($existingRunner | Out-String).Trim())) {
                throw "[FAIL-CLOSED] Designated runner container name '$RunnerContainerName' already exists! Refusing to force-remove existing runner."
            }
            $releaseDir = Split-Path -Parent $releaseAssembly
            $dockerArgs = @(
                "run", "--rm",
                "--pull=never",
                "--name", $RunnerContainerName,
                "--network", $ExpectedNetwork,
                "--cap-drop=ALL",
                "--workdir", "/results",
                "-v", "${releaseDir}:/testbin:ro",
                "-v", "${runsettings}:/cfg/test.runsettings:ro",
                "-v", "${resultsDir}:/results",
                "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
                "-e", "DOTNET_NOLOGO=1",
                "-e", "DOTNET_RUNNING_IN_CONTAINER=true",
                "-e", "UNIVERSAL_ERP_INTERNAL_CONTAINER_RUNNER=true",
                "-e", "UNIVERSAL_ERP_TEST_TARGET_HOST=$ExpectedHost",
                "-e", "UNIVERSAL_ERP_TEST_TARGET_PORT=$ExpectedPort",
                "-e", "UNIVERSAL_ERP_TEST_TARGET_DB=$ExpectedDatabase",
                "-e", "UNIVERSAL_ERP_TEST_CONNECTION_STRING=$env:UNIVERSAL_ERP_TEST_CONNECTION_STRING",
                "-e", "UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING=$env:UNIVERSAL_ERP_TEST_ADMIN_CONNECTION_STRING",
                "-e", "PGPASSWORD_ERP_APP=$env:PGPASSWORD_ERP_APP",
                "-e", "PGPASSWORD_ERP_DEV=$env:PGPASSWORD_ERP_DEV",
                $RunnerImage,
                "dotnet", "test", "/testbin/UniversalErp.Api.Tests.dll",
                "--settings", "/cfg/test.runsettings",
                "--filter", $Filter,
                "--logger", "trx;LogFileName=$trxFileName",
                "--results-directory", "/results"
            )
            & docker @dockerArgs | Out-Host
        } else {
            dotnet test $testProject `
                --configuration Release `
                --no-build `
                --settings $runsettings `
                --filter $Filter `
                --logger "trx;LogFileName=$trxFileName" `
                --results-directory $resultsDir | Out-Host
        }
        $exitCode = $LASTEXITCODE

        $executed = 0; $passed = 0; $failed = 0; $total = 0; $skipped = 0
        $failedTests = @()
        if (Test-Path $trxFullPath) {
            [xml]$trx = Get-Content $trxFullPath
            $c = $trx.TestRun.ResultSummary.Counters
            $total    = [int]$c.total
            $executed = [int]$c.executed
            $passed   = [int]$c.passed
            $failed   = [int]$c.failed
            $skipped  = [Math]::Max(0, $total - $executed)
            $failedNodes = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -ne "Passed" })
            foreach ($fn in $failedNodes) { $failedTests += "$($fn.testName) ($($fn.outcome))" }
        }

        $countOk = ($exitCode -eq 0 -and (Test-Path $trxFullPath) -and $failed -eq 0 -and $skipped -eq 0 -and $executed -eq $ExpectedExactTests -and $passed -eq $ExpectedExactTests)
        $color = if ($countOk) { "Green" } else { "Red" }
        $statusLabel = if ($countOk) { 'PASS' } else { 'FAIL' }
        Write-Host "[$statusLabel] ${SuiteName}: Executed=$executed Passed=$passed Failed=$failed Skipped=$skipped Expected=$ExpectedExactTests (ExitCode=$exitCode)" -ForegroundColor $color

        return [PSCustomObject]@{
            Suite       = $SuiteName
            Expected    = $ExpectedExactTests
            Total       = $total
            Executed    = $executed
            Passed      = $passed
            Failed      = $failed
            Skipped     = $skipped
            ExitCode    = $exitCode
            CountOk     = $countOk
            FailedTests = $failedTests
            TrxPath     = $trxFullPath
        }
    }

    $results = @()
    $results += Invoke-CurrentSuite -Filter "FullyQualifiedName~PostgresOfflineReceiptSyncIntegrationTests" -SuiteName "OfflineReceiptSync" -ExpectedExactTests 15
    $results += Invoke-CurrentSuite -Filter "FullyQualifiedName~PostgresSupplyChainOutboxIntegrationTests" -SuiteName "SupplyChainOutbox" -ExpectedExactTests 7
    $results += Invoke-CurrentSuite -Filter "FullyQualifiedName~PostgresSalesRepositoryIntegrationTests" -SuiteName "SalesRepository" -ExpectedExactTests 15
    $expectedTotal = 37
    if ($IncludeAdminCurrencySuite) {
        $results += Invoke-CurrentSuite -Filter "FullyQualifiedName~AdminCurrencySettingsPostgresTests" -SuiteName "AdminCurrencySettings" -ExpectedExactTests 1
        $expectedTotal = 38
    }

    $sumExpected = ($results | Measure-Object -Property Expected -Sum).Sum
    $sumExecuted = ($results | Measure-Object -Property Executed -Sum).Sum
    $sumPassed   = ($results | Measure-Object -Property Passed -Sum).Sum
    $sumFailed   = ($results | Measure-Object -Property Failed -Sum).Sum
    $sumSkipped  = ($results | Measure-Object -Property Skipped -Sum).Sum
    $allOk       = ($results | Where-Object { -not $_.CountOk }).Count -eq 0

    Write-Host "`n====================================================================================" -ForegroundColor Cyan
    Write-Host "CURRENT-SCHEMA INTEGRATION LANE SUMMARY (Migrations 001..046)" -ForegroundColor Cyan
    Write-Host "====================================================================================" -ForegroundColor Cyan
    foreach ($r in $results) {
        $lineColor = if ($r.CountOk) { "Green" } else { "Red" }
        Write-Host ("{0,-22} Executed={1,2}  Passed={2,2}  Failed={3,2}  Skipped={4,2}  Expected={5,2}  TRX={6}" -f $r.Suite, $r.Executed, $r.Passed, $r.Failed, $r.Skipped, $r.Expected, (Split-Path -Leaf $r.TrxPath)) -ForegroundColor $lineColor
        foreach ($ft in $r.FailedTests) { Write-Host "   -> $ft" -ForegroundColor Red }
    }
    Write-Host "------------------------------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host ("{0,-22} Executed={1,2}  Passed={2,2}  Failed={3,2}  Skipped={4,2}  Expected={5,2}" -f "TOTAL", $sumExecuted, $sumPassed, $sumFailed, $sumSkipped, $sumExpected) -ForegroundColor $(if ($allOk) { "Green" } else { "Red" })
    Write-Host "TRX Directory: $resultsDir" -ForegroundColor Cyan
    Write-Host "====================================================================================" -ForegroundColor Cyan

    if (-not $allOk -or $sumPassed -ne $expectedTotal -or $sumExpected -ne $expectedTotal) {
        exit 1
    }
    exit 0
}
catch {
    Write-Host "`n[FATAL] Current-Schema Runner Aborted: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
