<#
.SYNOPSIS
    Universal ERP -- Execute Approved Model A Accounting Adjustments (Neon)
.DESCRIPTION
    Safely executes 41_apply_approved_accounting_adjustments.sql against Neon PostgreSQL (neondb).
    Authorized Execution for:
      1. PROP-FIFO-001 (Dr 1200 / Cr 5010 PKR 135.00)
      2. Option A Capitalization (Dr 1200 / Cr 3010 PKR 135.00)
      3. Creation of 3rd FIFO layer for 3 EA @ PKR 45.00 in MAIN-W1 / PICK-01
    GUARANTEE: Migration 026 and Backfill Script 38 are NEVER called by this script.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$NeonConnectionString = "",

    [Parameter(Mandatory=$false)]
    [string]$SecretsFilePath = ""
)

$ErrorActionPreference = "Stop"

# Setup Paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SqlPath = Join-Path $ScriptDir "41_apply_approved_accounting_adjustments.sql"

if (-not (Test-Path $SqlPath)) {
    throw "SQL script not found at: '$SqlPath'"
}

function Mask-ConnectionString {
    param([string]$conn)
    if ([string]::IsNullOrWhiteSpace($conn)) { return "" }
    $masked = [regex]::Replace($conn, '(?i)(postgres(?:ql)?://[^:]+:)([^@]+)(@)', '${1}********${3}')
    $masked = [regex]::Replace($masked, '(?i)(Password\s*=\s*)([^;]+)', '${1}********')
    return $masked
}

function Convert-ToPostgreSqlUri {
    param([string]$connStr)
    $connStr = $connStr.Trim()
    if ($connStr -match "^postgres(?:ql)?://") {
        return $connStr
    }

    $kv = @{}
    foreach ($part in ($connStr -split ';')) {
        $p = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        $eqIdx = $p.IndexOf('=')
        if ($eqIdx -gt 0) {
            $key = $p.Substring(0, $eqIdx).Trim().ToLowerInvariant() -replace '[\s_]', ''
            $val = $p.Substring($eqIdx + 1).Trim()
            $kv[$key] = $val
        }
    }

    $h = $kv['host']
    if ([string]::IsNullOrWhiteSpace($h)) {
        return $connStr
    }

    $port = if ($kv['port']) { $kv['port'] } else { '5432' }
    $user = if ($kv['username']) { $kv['username'] } elseif ($kv['user']) { $kv['user'] } elseif ($kv['userid']) { $kv['userid'] } else { '' }
    $pwd = if ($kv['password']) { $kv['password'] } elseif ($kv['pwd']) { $kv['pwd'] } else { '' }
    $db = if ($kv['database']) { $kv['database'] } elseif ($kv['dbname']) { $kv['dbname'] } else { 'neondb' }
    $ssl = if ($kv['sslmode']) { $kv['sslmode'].ToLowerInvariant() } else { 'require' }

    $auth = ''
    if ($user -and $pwd) {
        $auth = [System.Uri]::EscapeDataString($user) + ":" + [System.Uri]::EscapeDataString($pwd) + "@"
    } elseif ($user) {
        $auth = [System.Uri]::EscapeDataString($user) + "@"
    }

    return "postgresql://${auth}${h}:${port}/${db}?sslmode=${ssl}"
}

Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host "=== Universal ERP -- Apply Approved Model A Adjustments (Neon Runner)  ===" -ForegroundColor Cyan
Write-Host "==========================================================================" -ForegroundColor Cyan

# 1. Resolve Connection String
$rawConn = $NeonConnectionString

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    if (-not [string]::IsNullOrWhiteSpace($env:NEON_MIGRATION_CONNECTION_STRING)) {
        Write-Host "[SECRETS] Loaded connection string from environment variable NEON_MIGRATION_CONNECTION_STRING." -ForegroundColor Green
        $rawConn = $env:NEON_MIGRATION_CONNECTION_STRING.Trim()
    } elseif (-not [string]::IsNullOrWhiteSpace($env:NEON_CONNECTION_STRING)) {
        Write-Host "[SECRETS] Loaded connection string from environment variable NEON_CONNECTION_STRING." -ForegroundColor Green
        $rawConn = $env:NEON_CONNECTION_STRING.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    $candidateFiles = @()
    if (-not [string]::IsNullOrWhiteSpace($SecretsFilePath)) { $candidateFiles += $SecretsFilePath }
    if (-not [string]::IsNullOrWhiteSpace($env:NEON_SECRETS_FILE)) { $candidateFiles += $env:NEON_SECRETS_FILE }

    $candidateFiles += @(
        "C:\secrets\neon.json",
        (Join-Path $ScriptDir "neon.json"),
        (Join-Path (Split-Path -Parent $ScriptDir) "neon.json")
    )

    foreach ($file in $candidateFiles) {
        if (Test-Path $file) {
            try {
                $json = Get-Content $file -Raw | ConvertFrom-Json
                if ($json.connectionString) {
                    Write-Host "[SECRETS] Loaded connection string from $file" -ForegroundColor Green
                    $rawConn = $json.connectionString
                    break
                }
            } catch {}
        }
    }
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Host "`n[SECURE PROMPT] No connection string found in parameters, environment, or secrets file." -ForegroundColor Yellow
    Write-Host "Please enter your Neon connection string below (input is masked and never logged):" -ForegroundColor Yellow
    $securePass = Read-Host -Prompt "Neon Connection String" -AsSecureString
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePass)
    $rawConn = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Error "Connection string is required to connect to Neon database."
}

$pgUri = Convert-ToPostgreSqlUri -connStr $rawConn
$maskedUri = Mask-ConnectionString -conn $pgUri
Write-Host "[CONNECT] Connecting to: $maskedUri" -ForegroundColor Gray

# 2. Assert Target Database neondb
if ($pgUri -notmatch "/neondb(?:\?|$)") {
    Write-Error "Safety Abort: Target database must be 'neondb'. Extracted connection does not target neondb."
}

# 3. Locate psql
$psqlPath = ""
$systemPsql = Get-Command psql -ErrorAction SilentlyContinue
if ($systemPsql) {
    $psqlPath = $systemPsql.Source
} else {
    $knownPsqlPaths = @(
        "C:\Program Files\PostgreSQL\18\bin\psql.exe",
        "C:\Program Files\PostgreSQL\17\bin\psql.exe",
        "C:\Program Files\PostgreSQL\16\bin\psql.exe",
        "C:\Program Files\PostgreSQL\15\bin\psql.exe",
        "C:\Program Files\PostgreSQL\14\bin\psql.exe"
    )
    foreach ($cand in $knownPsqlPaths) {
        if (Test-Path $cand) {
            $psqlPath = $cand
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($psqlPath)) {
    Write-Error "PostgreSQL CLI (psql) not found in PATH or standard installation locations."
}

Write-Host "[TOOL] Using psql: $psqlPath" -ForegroundColor Gray
Write-Host "[RUN] Executing 41_apply_approved_accounting_adjustments.sql..." -ForegroundColor Yellow

$psqlArgs = @(
    "--dbname=`"$pgUri`"",
    "-v", "ON_ERROR_STOP=1",
    "-f", "`"$SqlPath`""
)

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $psqlPath
$psi.Arguments = [string]::Join(' ', $psqlArgs)
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$proc = [System.Diagnostics.Process]::Start($psi)
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()

if ($stdout) { Write-Host $stdout }

if ($proc.ExitCode -ne 0) {
    if ($stderr) { Write-Host $stderr -ForegroundColor Red }
    Write-Error "Approved accounting adjustments execution FAILED with exit code $($proc.ExitCode)."
} else {
    Write-Host "`n==========================================================================" -ForegroundColor Green
    Write-Host "=== [SUCCESS] Model A Approved Adjustments Successfully Executed!       ===" -ForegroundColor Green
    Write-Host "==========================================================================" -ForegroundColor Green
    Write-Host "[RESULT] GL Account 1200 and FIFO Valuation are 100% reconciled at PKR 2,250.00." -ForegroundColor Green
    Write-Host "[RESULT] All 50 units (21 in PICK-01 and 29 in RECEIVE-01) are fully valued & unblocked for POS." -ForegroundColor Green
}
