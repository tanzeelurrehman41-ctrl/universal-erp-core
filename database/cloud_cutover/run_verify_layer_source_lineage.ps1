<#
.SYNOPSIS
    Universal ERP -- Run Source Lineage Verification Script 43 on Neon
.DESCRIPTION
    Safely executes 43_verify_layer_source_lineage.sql against Neon PostgreSQL (neondb).
    Strictly read-only source lineage audit runner.
    Asserts:
      - Layer 1: e4a1c001-2024-4001-8001-41a9d7900001 (GRN-2024-01: Orig 20 EA, Rem 18 EA)
      - Layer 2: e4a1c002-2024-4003-8001-07da98290001 (GRN-2024-03: Orig 30 EA, Rem 29 EA)
      - Layer 3: c3333333-3333-4333-8333-333333333333 (PROP-CAP-001: Orig 3 EA, Rem 3 EA)
      - Total 50 EA valued inventory
      - PKR 2,250.00 FIFO valuation
      - Account 1200 balance = PKR 2,250.00
      - Variance = PKR 0.00 (Zero variance)
      - JRN-ADJ-PROP-FIFO-001 and JRN-ADJ-PROP-CAP-001 posted exactly once
    GUARANTEE: Strictly read-only; no migrations, backfills, or data modifications.
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
$VerifySqlPath = Join-Path $ScriptDir "43_verify_layer_source_lineage.sql"

if (-not (Test-Path $VerifySqlPath)) {
    throw "Verification SQL script not found at: '$VerifySqlPath'"
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
Write-Host "=== Universal ERP -- Source Lineage Verification (Script 43 Runner)    ===" -ForegroundColor Cyan
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
Write-Host "[CONNECT] Connecting to: $maskedUri" -ForegroundColor Cyan

# 2. Locate psql
$psqlPath = ""
$knownPsqlPaths = @(
    "C:\Program Files\PostgreSQL\18\bin\psql.exe",
    "C:\Program Files\PostgreSQL\17\bin\psql.exe",
    "C:\Program Files\PostgreSQL\16\bin\psql.exe",
    "C:\Program Files\PostgreSQL\15\bin\psql.exe",
    "C:\Program Files\PostgreSQL\14\bin\psql.exe"
)

foreach ($p in $knownPsqlPaths) {
    if (Test-Path $p) {
        $psqlPath = $p
        break
    }
}

if (-not $psqlPath) {
    $cmd = Get-Command "psql" -ErrorAction SilentlyContinue
    if ($cmd) {
        $psqlPath = $cmd.Source
    }
}

if (-not $psqlPath) {
    throw "PostgreSQL CLI (psql) not found in PATH or standard installation locations."
}

Write-Host "[TOOL] Using psql: $psqlPath" -ForegroundColor DarkGray
Write-Host "[RUN] Executing 43_verify_layer_source_lineage.sql..." -ForegroundColor Yellow

$psqlOutput = & $psqlPath "$pgUri" -f "$VerifySqlPath" -v "ON_ERROR_STOP=1" 2>&1

$hasError = $false
foreach ($line in $psqlOutput) {
    $lineStr = "$line"
    if ($lineStr -match "(?i)ERROR:" -or $lineStr -match "(?i)FATAL:") {
        Write-Host $lineStr -ForegroundColor Red
        $hasError = $true
    } elseif ($lineStr -match "\[SUCCESS\]" -or $lineStr -match "COMMIT") {
        Write-Host $lineStr -ForegroundColor Green
    } else {
        Write-Host $lineStr
    }
}

if ($LASTEXITCODE -ne 0 -or $hasError) {
    throw "Post-Approval Source Lineage Verification FAILED with exit code $LASTEXITCODE."
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== PASS: All Source Lineages and Post-Approval Invariants Reconciled! ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
