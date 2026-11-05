# universal-erp-core/run_controlled_tests.ps1
# Master Controlled Test Runner with Fail-Closed Preflight Enforcement
param(
    [string]$ExpectedHost = "127.0.0.1",
    [int]$ExpectedPort = 5434,
    [string]$ExpectedDatabase = "universal_erp_disposable_test",
    [string]$ExpectedContainer = "universal-erp-postgres-test",
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
    [string]$RlsAuditSqlPath = "C:\backups\audit_rls_policies.sql",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

# 1. Enforce PowerShell 7+ (.NET 10 Runtime Compatibility)
$psMajor = $PSVersionTable.PSVersion.Major
if ($psMajor -lt 7) {
    Write-Host @"
====================================================================================
[FAIL-CLOSED] INCOMPATIBLE POWERSHELL RUNTIME DETECTED!
Current Shell: PowerShell $($PSVersionTable.PSVersion)
Current .NET Runtime: $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)
Required Shell: PowerShell 7+ (pwsh.exe, running on .NET Core / .NET 10)
Reason: UniversalErp.Api.Tests targets net10.0. Loading Npgsql.dll requires a .NET host.
Resolution: Execute this runner using 'pwsh run_controlled_tests.ps1' under PowerShell 7+.
====================================================================================
"@ -ForegroundColor Red
    exit 1
}

Write-Host "Runner Runtime Verified: PowerShell $($PSVersionTable.PSVersion) on $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)" -ForegroundColor Green

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
$solutionPath = Join-Path $repoRoot "src\backend\UniversalErp.sln"
$testProject = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\UniversalErp.Api.Tests.csproj"
$runsettings = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\test.runsettings"
$resultsDir = Join-Path $repoRoot "test-results"
$releaseAssembly = Join-Path $repoRoot "src\backend\tests\UniversalErp.Api.Tests\bin\Release\net10.0\UniversalErp.Api.Tests.dll"
$preflightScript = Join-Path $repoRoot "preflight_test_env_guard.ps1"

# Assert Source Files Exist
if (-not (Test-Path $solutionPath)) { Write-Host "[FAIL-CLOSED] Solution file not found at '$solutionPath'!" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $testProject)) { Write-Host "[FAIL-CLOSED] Test project not found at '$testProject'!" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $runsettings)) { Write-Host "[FAIL-CLOSED] Runsettings file not found at '$runsettings'!" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $preflightScript)) { Write-Host "[FAIL-CLOSED] Preflight script not found at '$preflightScript'!" -ForegroundColor Red; exit 1 }

try {
    # 2. Release Compilation Phase (Explicitly Non-Incremental)
    if (-not $SkipBuild) {
        Write-Host "`n=== [BUILD] Compiling Solution in Release Mode (Non-Incremental) ===" -ForegroundColor Cyan
        $buildStartTimeUtc = [DateTime]::UtcNow.AddSeconds(-2)

        dotnet build $solutionPath --configuration Release --no-incremental --nologo
        $buildRc = $LASTEXITCODE
        if ($buildRc -ne 0) { throw "'dotnet build -c Release --no-incremental' failed with exit code $buildRc." }
        if (-not (Test-Path $releaseAssembly)) { throw "Expected Release test assembly does not exist at '$releaseAssembly'!" }
        $assemblyItem = Get-Item $releaseAssembly
        if ($assemblyItem.LastWriteTimeUtc -lt $buildStartTimeUtc) { throw "Release assembly was not updated during build!" }
        Write-Host "[PASS] Solution built successfully in Release mode." -ForegroundColor Green
    } else {
        if (-not (Test-Path $releaseAssembly)) { throw "[FAIL-CLOSED] SkipBuild specified but Release test assembly does not exist!" }
        Write-Host "[INFO] SkipBuild requested. Verified existing Release assembly at '$releaseAssembly'." -ForegroundColor Yellow
    }

    # 3. Protected Preflight Execution (Isolated Child Process via pwsh)
    Write-Host "`n=== [PREFLIGHT] Invoking Preflight Guard (Isolated pwsh Child Process) ===" -ForegroundColor Cyan
    $preflightArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", "`"$preflightScript`"",
        "-ExpectedHost", $ExpectedHost,
        "-ExpectedPort", $ExpectedPort,
        "-ExpectedDatabase", $ExpectedDatabase,
        "-ExpectedContainer", $ExpectedContainer,
        "-ExpectedNetwork", $ExpectedNetwork,
        "-ExpectedVolume", $ExpectedVolume,
        "-ExpectedAppUser", $ExpectedAppUser,
        "-ExpectedAdminUser", $ExpectedAdminUser,
        "-BackupFilePath", $BackupFilePath,
        "-ManifestPath", $ManifestPath,
        "-SignaturePath", $SignaturePath,
        "-PublicKeyPath", $PublicKeyPath,
        "-AuditSqlPath", $AuditSqlPath,
        "-RlsAuditSqlPath", $RlsAuditSqlPath
    )
    if (-not [string]::IsNullOrWhiteSpace($TrustedKeyFingerprint)) {
        $preflightArgs += @("-TrustedKeyFingerprint", $TrustedKeyFingerprint)
    }

    $preflightProc = Start-Process -FilePath "pwsh.exe" -ArgumentList $preflightArgs -NoNewWindow -Wait -PassThru
    $preflightRc = $preflightProc.ExitCode

    if ($preflightRc -ne 0) {
        throw "Preflight test environment guard failed with exit code $preflightRc. Test execution STRICTLY FORBIDDEN."
    }
    Write-Host "[PASS] All 12 Preflight gates verified. Target environment approved for test execution." -ForegroundColor Green

    # 4. Controlled Test Runner Function
    function Invoke-ControlledTestRun {
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
        if (Test-Path $trxFullPath) {
            throw "[FAIL-CLOSED] Collision detected: target TRX file already exists at '$trxFullPath'!"
        }

        Write-Host "`n>>> [RUNNING] $SuiteName | Filter: $Filter" -ForegroundColor Cyan

        dotnet test $testProject `
            --configuration Release `
            --no-build `
            --settings $runsettings `
            --filter $Filter `
            --logger "trx;LogFileName=$trxFileName" `
            --results-directory $resultsDir

        $testRc = $LASTEXITCODE
        if ($testRc -ne 0) {
            throw "Test suite '$SuiteName' FAILED with exit code $testRc!"
        }

        if (-not (Test-Path $trxFullPath)) {
            throw "TRX results file not generated at '$trxFullPath'!"
        }

        [xml]$trx = Get-Content $trxFullPath
        $executed = [int]$trx.TestRun.ResultSummary.Counters.executed
        $passed = [int]$trx.TestRun.ResultSummary.Counters.passed
        $failed = [int]$trx.TestRun.ResultSummary.Counters.failed

        if ($failed -gt 0 -or $passed -ne $ExpectedExactTests -or $executed -ne $ExpectedExactTests) {
            throw "Suite '$SuiteName' count assertion failed! Expected $ExpectedExactTests passed, got Executed=$executed, Passed=$passed, Failed=$failed."
        }

        Write-Host "[PASS] $SuiteName passed: $passed/$ExpectedExactTests executed cleanly." -ForegroundColor Green
        return [PSCustomObject]@{
            Suite    = $SuiteName
            Total    = $ExpectedExactTests
            Passed   = $passed
            Failed   = $failed
            TrxPath  = $trxFullPath
        }
    }

    # 5. Execute All Three Controlled Integration Test Suites
    $results = @()
    $results += Invoke-ControlledTestRun `
        -Filter "FullyQualifiedName~PostgresOfflineReceiptSyncIntegrationTests" `
        -SuiteName "OfflineReceiptSync" `
        -ExpectedExactTests 15

    $results += Invoke-ControlledTestRun `
        -Filter "FullyQualifiedName~PostgresSupplyChainOutboxIntegrationTests" `
        -SuiteName "SupplyChainOutbox" `
        -ExpectedExactTests 7

    $results += Invoke-ControlledTestRun `
        -Filter "FullyQualifiedName~PostgresSalesRepositoryIntegrationTests" `
        -SuiteName "SalesRepository" `
        -ExpectedExactTests 10

    Write-Host @"

====================================================================================
PHASE 4 CONTROLLED TEST EXECUTION COMPLETE — ALL SUITES PASSED
====================================================================================
Offline Receipt Sync: 15 / 15 Passed
Supply Chain Outbox:   7 /  7 Passed
Sales Repository:     10 / 10 Passed
Total Executed:       32 / 32 Passed (Zero Failures, Zero Skipped)
TRX Directory:        $resultsDir
====================================================================================
"@ -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "`n[FATAL] Controlled Test Runner Aborted: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
