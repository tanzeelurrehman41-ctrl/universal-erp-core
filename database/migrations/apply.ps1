[CmdletBinding()]
param(
    [switch]$Execute,
    [string]$TargetContainer = "universal-erp-postgres-test",
    [string]$TargetDatabase = "universal_erp_disposable_test",
    [string]$TargetUser = "erp_dev",
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PublicKeyPath,
    [string]$TrustedKeyFingerprint
)

$ErrorActionPreference = "Stop"

# ── Approved Disposable Test Environment Constants ────────────────────────────
$APPROVED_TARGET_CONTAINER = "universal-erp-postgres-test"
$APPROVED_TARGET_DATABASE  = "universal_erp_disposable_test"
$APPROVED_TARGET_USER      = "erp_dev"
$APPROVED_NETWORK          = "universal-erp-test-net"
$APPROVED_VOLUME           = "erp_postgres_test_data"
$APPROVED_HOST_PORT        = 5434
$APPROVED_IMAGE_DIGEST     = "sha256:4a3dfb128540b616ec485d2da8a96495db6ea0fa4f8f4803ea6ff910c2ec857c"
$AdvisoryLockId            = 714209

# ── Target Parameter Validation (Strict Fail-Closed) ───────────────────────────
if ($TargetContainer -ne $APPROVED_TARGET_CONTAINER -or
    $TargetDatabase -ne $APPROVED_TARGET_DATABASE -or
    $TargetUser -ne $APPROVED_TARGET_USER) {
    throw "[FAIL-CLOSED] Target overrides are strictly forbidden. Target must match approved disposable test environment constants: Container='$APPROVED_TARGET_CONTAINER', DB='$APPROVED_TARGET_DATABASE', User='$APPROVED_TARGET_USER'"
}

# ── Target Container Inspection (Fail-Closed Before Any Connection) ───────────
$cJson = docker inspect $APPROVED_TARGET_CONTAINER 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cJson)) {
    throw "[FAIL-CLOSED] Approved test container '$APPROVED_TARGET_CONTAINER' does not exist or cannot be inspected!"
}

$cData = $cJson | ConvertFrom-Json
if ($cData.Count -gt 0) { $cData = $cData[0] }

if (-not $cData.State.Running) {
    throw "[FAIL-CLOSED] Container '$APPROVED_TARGET_CONTAINER' is not running!"
}

# Image Digest Verification
$imageDigestMatched = $false
$imgJson = docker image inspect $cData.Image 2>$null
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($imgJson)) {
    $imgData = $imgJson | ConvertFrom-Json
    if ($imgData.Count -gt 0) { $imgData = $imgData[0] }
    if ($imgData.RepoDigests) {
        foreach ($rd in $imgData.RepoDigests) {
            if ($rd -like "*@$APPROVED_IMAGE_DIGEST*") { $imageDigestMatched = $true; break }
        }
    }
}
if (-not $imageDigestMatched) {
    throw "[FAIL-CLOSED] Container '$APPROVED_TARGET_CONTAINER' image does not match pinned digest '$APPROVED_IMAGE_DIGEST'!"
}

# Isolated Network Verification
if (-not $cData.NetworkSettings.Networks.$APPROVED_NETWORK) {
    throw "[FAIL-CLOSED] Container '$APPROVED_TARGET_CONTAINER' is not attached to isolated network '$APPROVED_NETWORK'!"
}

# Dedicated Volume Mount Verification
$hasApprovedMount = $false
foreach ($m in $cData.Mounts) {
    if ($m.Name -eq $APPROVED_VOLUME -or $m.Source -like "*$APPROVED_VOLUME*") {
        $hasApprovedMount = $true
        break
    }
}
if (-not $hasApprovedMount) {
    throw "[FAIL-CLOSED] Container '$APPROVED_TARGET_CONTAINER' is not mounted with dedicated volume '$APPROVED_VOLUME'!"
}

# Loopback Port Binding Verification
$p5432 = $cData.NetworkSettings.Ports."5432/tcp"
if (-not $p5432 -or $p5432.Count -eq 0 -or $p5432[0].HostIp -ne "127.0.0.1" -or [int]$p5432[0].HostPort -ne $APPROVED_HOST_PORT) {
    throw "[FAIL-CLOSED] Container '$APPROVED_TARGET_CONTAINER' port 5432 is not strictly bound to loopback 127.0.0.1:$APPROVED_HOST_PORT!"
}

$MigrationDir = $PSScriptRoot
$approvedManifestNames = @(
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
    "014_finance_chart_of_accounts_seed.sql"
)

$driftProbes = @{
    "015_sales_and_pos" = "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'erp_sales' AND tablename = 'customer');"
    "016_sales_seed" = "SELECT EXISTS (SELECT 1 FROM erp_sales.customer WHERE customer_code = 'CUST-WALKIN');"
    "017_offline_sync_hardening" = "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'sync' AND table_name = 'operation_inbox' AND column_name = 'claim_token');"
    "018_command_idempotency_company_scope_and_conflicts" = "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'sync' AND tablename = 'operation_conflicts');"
    "019_purchasing_outbox_dead_letter" = "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'erp_purchasing' AND table_name = 'outbox_event' AND column_name = 'status');"
}

# ── Format 2 Signed Migration Manifest & Cryptographic Verification ────────────
$manifestChecksums = @{}
if ($Execute) {
    if (-not $ManifestPath -or -not $SignaturePath -or -not $PublicKeyPath -or -not $TrustedKeyFingerprint) {
        throw "[FAIL-CLOSED] Execution mode requires -ManifestPath, -SignaturePath, -PublicKeyPath, and -TrustedKeyFingerprint!"
    }
    if (-not (Test-Path -LiteralPath $ManifestPath) -or -not (Test-Path -LiteralPath $SignaturePath) -or -not (Test-Path -LiteralPath $PublicKeyPath)) {
        throw "[FAIL-CLOSED] Required cryptographic files missing on disk!"
    }

    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem((Get-Content -LiteralPath $PublicKeyPath -Raw))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $pubDer = $rsa.ExportSubjectPublicKeyInfo()
    $pubFp = [BitConverter]::ToString($sha.ComputeHash($pubDer)).Replace("-", "").ToLowerInvariant()
    if ($pubFp -ne $TrustedKeyFingerprint.ToLowerInvariant()) {
        throw "[FAIL-CLOSED] Public key fingerprint mismatch! Expected: $TrustedKeyFingerprint, Got: $pubFp"
    }

    $manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
    $signatureBytes = [System.IO.File]::ReadAllBytes($SignaturePath)
    $sigValid = $rsa.VerifyData($manifestBytes, $signatureBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    if (-not $sigValid) {
        throw "[FAIL-CLOSED] Detached RSA signature verification failed for migration manifest!"
    }

    $manifestLines = Get-Content -LiteralPath $ManifestPath
    foreach ($line in $manifestLines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) { continue }
        if ($trimmed -notmatch '^([a-f0-9]{64})\s+([0-9]{3}_[a-zA-Z0-9_\-]+\.sql)$') {
            throw "[FAIL-CLOSED] Malformed line in Format 2 migration checksum manifest: '$trimmed'"
        }
        $fHash = $Matches[1].ToLowerInvariant()
        $fName = $Matches[2]
        if ($manifestChecksums.ContainsKey($fName)) {
            throw "[FAIL-CLOSED] Duplicate migration entry in manifest: $fName"
        }
        $manifestChecksums[$fName] = $fHash
    }

    foreach ($approvedName in $approvedManifestNames) {
        if (-not $manifestChecksums.ContainsKey($approvedName)) {
            throw "[FAIL-CLOSED] Approved historical migration missing from manifest: $approvedName"
        }
        $diskFile = Join-Path $MigrationDir $approvedName
        if (-not (Test-Path -LiteralPath $diskFile)) {
            throw "[FAIL-CLOSED] Approved historical migration missing on disk: $approvedName"
        }
        $diskBytes = [System.IO.File]::ReadAllBytes($diskFile)
        $diskHash = [BitConverter]::ToString($sha.ComputeHash($diskBytes)).Replace("-", "").ToLowerInvariant()
        if ($diskHash -ne $manifestChecksums[$approvedName]) {
            throw "[FAIL-CLOSED] Tamper detected! Migration file $approvedName checksum divergence from manifest!"
        }
    }
    Write-Host "[VERIFIED] All 14 historical migration files cryptographically match signed DBA manifest." -ForegroundColor Green
}

# ── Persistent Interactive psql Process ────────────────────────────────────────
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "docker"
$psi.Arguments = "exec -i $TargetContainer psql -X -v ON_ERROR_STOP=1 -U $TargetUser -d $TargetDatabase"
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$psqlProc = [System.Diagnostics.Process]::Start($psi)
if (-not $psqlProc) { throw "[FAIL-CLOSED] Failed to launch interactive psql process!" }

$stderrQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$psqlProc.ErrorDataReceived += {
    param($sender, $e)
    if ($e.Data) { $stderrQueue.Enqueue($e.Data) }
}
$psqlProc.BeginErrorReadLine()

function Invoke-PsqlSessionCommand {
    param(
        [Parameter(Mandatory=$true)][string]$CommandText,
        [switch]$IsMetaCommand,
        [int]$TimeoutSeconds = 30
    )
    $sentinel = "__SENTINEL_" + [guid]::NewGuid().ToString("N") + "__"
    if ($IsMetaCommand) {
        $psqlProc.StandardInput.WriteLine($CommandText)
    } else {
        $psqlProc.StandardInput.WriteLine("$CommandText;")
    }
    $psqlProc.StandardInput.WriteLine("\echo $sentinel")
    $psqlProc.StandardInput.Flush()

    $outputLines = [System.Collections.Generic.List[string]]::new()
    $cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))

    try {
        while ($true) {
            $readTask = $psqlProc.StandardOutput.ReadLineAsync($cts.Token)
            $line = $readTask.GetAwaiter().GetResult()
            if ($null -eq $line) {
                $errText = ($stderrQueue.ToArray() -join "`n")
                throw "[FAIL-CLOSED] Interactive psql session terminated unexpectedly (EOF). Stderr: $errText"
            }
            if ($line.Trim() -eq $sentinel) {
                break
            }
            $outputLines.Add($line)
        }
    } catch [System.OperationCanceledException] {
        try { $psqlProc.Kill() } catch {}
        throw "[TIMEOUT] psql session timed out after $TimeoutSeconds seconds waiting for sentinel response!"
    } finally {
        $cts.Dispose()
    }
    return $outputLines
}

try {
    # Acquire Session Advisory Lock
    $lockLines = Invoke-PsqlSessionCommand -CommandText "SELECT pg_try_advisory_lock($AdvisoryLockId)"
    $lockAcquired = ($lockLines | Where-Object { $_ -match '^\s*([tf])\s*$' } | ForEach-Object { $Matches[1] })
    if ($lockAcquired -ne "t") {
        throw "[FAIL-CLOSED] Could not acquire persistent session advisory lock ($AdvisoryLockId). Another runner is active!"
    }
    Write-Host "[LOCKED] Persistent advisory lock $AdvisoryLockId acquired successfully." -ForegroundColor Green

    # Assert Connection Identity
    $idLines = Invoke-PsqlSessionCommand -CommandText "SELECT current_database(), current_user"
    $dbMatch = $idLines | Where-Object { $_ -match '^\s*(\w+)\s*\|\s*(\w+)\s*$' }
    if (-not $dbMatch -or $Matches[1] -ne $TargetDatabase -or $Matches[2] -ne $TargetUser) {
        throw "[FAIL-CLOSED] Target connection verification failed! Expected DB: $TargetDatabase, User: $TargetUser"
    }

    # Read-Only Catalog & Ledger Pre-Mutation Inspection
    $tblCheck = Invoke-PsqlSessionCommand -CommandText "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'schema_migrations')"
    $hasTable = ($tblCheck | Where-Object { $_ -match '^\s*([tf])\s*$' } | ForEach-Object { $Matches[1] }) -eq "t"

    $hasChecksumColumn = $false
    $ledgerRecords = [System.Collections.Generic.Dictionary[string, string]]::new()
    $appliedVersions = [System.Collections.Generic.List[string]]::new()

    if ($hasTable) {
        $colCheck = Invoke-PsqlSessionCommand -CommandText "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'schema_migrations' AND column_name = 'checksum')"
        $hasChecksumColumn = ($colCheck | Where-Object { $_ -match '^\s*([tf])\s*$' } | ForEach-Object { $Matches[1] }) -eq "t"

        if (-not $hasChecksumColumn) {
            throw "[FAIL-CLOSED] public.schema_migrations lacks mandatory 'checksum' column! Historical ledger cannot be verified. Automatic schema mutation is strictly forbidden. A separately approved, evidence-backed DBA migration and historical checksum backfill is required."
        }

        $countLines = Invoke-PsqlSessionCommand -CommandText "SELECT count(*) FROM public.schema_migrations"
        $dbCountMatch = ($countLines | Where-Object { $_ -match '^\s*(\d+)\s*$' } | ForEach-Object { [int]$Matches[1] })
        if ($null -eq $dbCountMatch) {
            throw "[FAIL-CLOSED] Failed to query row count from public.schema_migrations!"
        }
        $dbRowCount = $dbCountMatch

        $rows = Invoke-PsqlSessionCommand -CommandText "SELECT 'ROW::' || version || '::' || coalesce(checksum, '') FROM public.schema_migrations ORDER BY version"
        $parsedRowCount = 0

        foreach ($r in $rows) {
            $trimmed = $r.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
            if ($trimmed -match '^\?column\?$' -or $trimmed -match '^-+$' -or $trimmed -match '^\(\d+\s+rows?\)$') {
                continue
            }
            if ($trimmed -notmatch '^ROW::') {
                throw "[FAIL-CLOSED] Malformed or unparseable output from ledger query: '$trimmed'"
            }
            if ($trimmed -notmatch '^ROW::(\d{3}_[A-Za-z0-9_-]+)::([a-f0-9]{64})$') {
                throw "[FAIL-CLOSED] Malformed ledger row detected: '$trimmed'. Every record must have valid version and non-empty 64-hex SHA-256 checksum!"
            }

            $v = $Matches[1]
            $chk = $Matches[2]

            if ($ledgerRecords.ContainsKey($v)) {
                throw "[FAIL-CLOSED] Duplicate migration version in public.schema_migrations: '$v'"
            }

            $ledgerRecords[$v] = $chk
            $appliedVersions.Add($v)
            $parsedRowCount++
        }

        if ($parsedRowCount -ne $dbRowCount) {
            throw "[FAIL-CLOSED] Ledger count mismatch: parsed $parsedRowCount rows, but database count(*) reported $dbRowCount rows!"
        }

        # Validate Contiguous Historical Sequence (001 through K, K <= 14)
        for ($i = 0; $i -lt $appliedVersions.Count; $i++) {
            $expectedNum = $i + 1
            $v = $appliedVersions[$i]
            if ($v -match '^0*(\d+)_') {
                $actualNum = [int]$Matches[1]
                if ($actualNum -ne $expectedNum) {
                    throw "[FAIL-CLOSED] Non-contiguous historical ledger sequence detected! Expected version index $expectedNum, found $actualNum ($v). Gaps in migration history are forbidden!"
                }
            } else {
                throw "[FAIL-CLOSED] Malformed version format in public.schema_migrations: $v"
            }
        }

        # Strict Cross-Verification against Signed DBA Manifest
        if ($Execute) {
            foreach ($v in $appliedVersions) {
                $manifestKey = "$v.sql"
                if (-not $manifestChecksums.ContainsKey($manifestKey)) {
                    throw "[FAIL-CLOSED] Applied migration '$v' recorded in ledger has no matching entry in signed DBA manifest!"
                }
                $expectedManifestHash = $manifestChecksums[$manifestKey]
                $storedHash = $ledgerRecords[$v]
                if ($storedHash -ne $expectedManifestHash) {
                    throw "[FAIL-CLOSED] Ledger checksum divergence! Migration '$v' stored hash '$storedHash' does not match signed DBA manifest hash '$expectedManifestHash'!"
                }
            }
        }
    }

    # Discover Pending Migrations
    $allFiles = Get-ChildItem -LiteralPath $MigrationDir -File -Filter "*.sql" |
        Where-Object { $_.BaseName -match '^\d{3}_[A-Za-z0-9_-]+$' } |
        Sort-Object Name

    $pendingToApply = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

    foreach ($file in $allFiles) {
        $vName = $file.BaseName

        # Absolute Rejection of Unauthorized Migrations 015-019
        if ($vName -match '^0*(\d+)') {
            $vNum = [int]$Matches[1]
            if ($vNum -ge 15 -and $vNum -le 19) {
                throw "[FAIL-CLOSED] Migration $vName (v$vNum) is strictly forbidden! Migrations 015-019 are unauthorized for runner execution or ledger recording."
            }
        }

        if ($ledgerRecords.ContainsKey($vName)) {
            continue
        }

        # Out-of-band Catalog Drift Probing
        if ($driftProbes.ContainsKey($vName)) {
            $probeSql = $driftProbes[$vName]
            $probeRes = Invoke-PsqlSessionCommand -CommandText $probeSql
            $objExists = ($probeRes | Where-Object { $_ -match '^\s*([tf])\s*$' } | ForEach-Object { $Matches[1] }) -eq "t"
            if ($objExists) {
                throw "[FAIL-CLOSED] Catalog drift detected! Signature object for unrecorded migration '$vName' already exists in database catalog. Out-of-band manual execution detected!"
            }
        }

        $pendingToApply.Add($file)
    }

    # Non-Mutating Dry-Run Complete
    if (-not $Execute) {
        Write-Host "`n========================================================================" -ForegroundColor Cyan
        Write-Host "[DRY-RUN COMPLETE] Target: $TargetDatabase on container $TargetContainer" -ForegroundColor Cyan
        Write-Host "Applied Ledger Count: $($appliedVersions.Count) | Pending Count: $($pendingToApply.Count)" -ForegroundColor Cyan
        foreach ($p in $pendingToApply) {
            Write-Host "  -> Planned to apply: $($p.BaseName)" -ForegroundColor Yellow
        }
        Write-Host "No changes were made to the database. Re-run with -Execute to apply." -ForegroundColor Cyan
        Write-Host "========================================================================" -ForegroundColor Cyan
        return
    }

    # Execute Pending Migrations
    foreach ($migration in $pendingToApply) {
        $vName = $migration.BaseName
        if ($vName -match '^0*(\d+)') {
            $vNum = [int]$Matches[1]
            if ($vNum -ge 15 -and $vNum -le 19) {
                throw "[FAIL-CLOSED] Critical assertion: attempted execution of unauthorized Phase 4 migration $vName (v$vNum)!"
            }
        }

        $sha = [System.Security.Cryptography.SHA256]::Create()
        $fileBytes = [System.IO.File]::ReadAllBytes($migration.FullName)
        $fileHash = [BitConverter]::ToString($sha.ComputeHash($fileBytes)).Replace("-", "").ToLowerInvariant()

        $cTemp = "/tmp/universal-erp-$vName-$([guid]::NewGuid().ToString('N')).sql"
        & docker cp $migration.FullName "${TargetContainer}:$cTemp"
        if ($LASTEXITCODE -ne 0) { throw "[FAIL-CLOSED] Could not stage migration $vName into container!" }

        try {
            Write-Host "Applying: $vName ..." -ForegroundColor Yellow
            Invoke-PsqlSessionCommand -CommandText "BEGIN" | Out-Null
            Invoke-PsqlSessionCommand -CommandText "\i $cTemp" -IsMetaCommand | Out-Null
            Invoke-PsqlSessionCommand -CommandText "INSERT INTO public.schema_migrations (version, checksum) VALUES ('$vName', '$fileHash')" | Out-Null
            Invoke-PsqlSessionCommand -CommandText "COMMIT" | Out-Null
            Write-Host "[APPLIED] $vName committed successfully." -ForegroundColor Green
        } catch {
            Invoke-PsqlSessionCommand -CommandText "ROLLBACK" | Out-Null
            throw "[FAIL-CLOSED] Migration $vName failed execution! Transaction rolled back. Error: $($_.Exception.Message)"
        } finally {
            & docker exec $TargetContainer rm -f $cTemp 2>$null | Out-Null
        }
    }
    Write-Host "[SUCCESS] All approved pending migrations applied cleanly." -ForegroundColor Green
} finally {
    try {
        Invoke-PsqlSessionCommand -CommandText "SELECT pg_advisory_unlock($AdvisoryLockId)" | Out-Null
        Invoke-PsqlSessionCommand -CommandText "\q" -IsMetaCommand | Out-Null
    } catch {}
    try {
        if (-not $psqlProc.HasExited) {
            $psqlProc.StandardInput.Close()
            $psqlProc.WaitForExit(5000)
        }
    } catch {}
    $psqlProc.Dispose()
}
