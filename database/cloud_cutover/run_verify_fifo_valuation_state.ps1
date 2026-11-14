<#
.SYNOPSIS
    Universal ERP -- Run Read-Only Verification Script 39 on Neon
.DESCRIPTION
    Safely executes 39_verify_fifo_valuation_state.sql against Neon PostgreSQL (neondb).
    Strictly read-only operational audit runner for Day 1-5 production monitoring.
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
$VerifySqlPath = Join-Path $ScriptDir "39_verify_fifo_valuation_state.sql"

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
Write-Host "=== Universal ERP -- Read-Only FIFO Verification (Script 39 Runner)    ===" -ForegroundColor Cyan
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

# 2. Strict Safety & Connection String Conversion
$psqlConn = Convert-ToPostgreSqlUri -connStr $rawConn

if ($psqlConn -match "localhost" -or $psqlConn -match "127\.0\.0\.1") {
    Write-Error "CRITICAL SAFETY VIOLATION: Execution against localhost / local production is strictly blocked!"
}

$masked = Mask-ConnectionString -conn $psqlConn
Write-Host "[TARGET] Remote Neon endpoint: $masked" -ForegroundColor Cyan

# 3. Resolve psql Executable
$psql = "psql"
try {
    $null = & $psql --version 2>&1
} catch {
    $found = $false
    $searchPaths = @(
        "C:\Program Files\PostgreSQL\*\bin\psql.exe",
        "C:\Program Files (x86)\PostgreSQL\*\bin\psql.exe"
    )
    foreach ($pathPattern in $searchPaths) {
        $matches = Get-Item $pathPattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
        if ($matches) {
            $psql = $matches[0].FullName
            $found = $true
            break
        }
    }
    if (-not $found) {
        Write-Error "psql CLI tool not found in PATH or standard PostgreSQL install locations."
    }
}

# 4. Verify Remote Database
Write-Host "`n[STEP 1/2] Verifying remote Neon database..." -ForegroundColor Yellow
$verifyCmd = "SELECT current_database(), current_user, version();"
$verifyOutput = & $psql "$psqlConn" -c "$verifyCmd" -t -A 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to connect to Neon database. Error output: $verifyOutput"
}

$dbParts = $verifyOutput -split '\|'
$dbName = $dbParts[0].Trim()
$dbUser = $dbParts[1].Trim()
$pgVer = $dbParts[2].Trim()

if ($dbName -ne "neondb") {
    Write-Error "Remote database assertion failed! Connected to: '$verifyOutput'"
}

Write-Host "Connected Database : $dbName" -ForegroundColor Green
Write-Host "Database User      : $dbUser" -ForegroundColor Green
Write-Host "Cluster Version    : $pgVer" -ForegroundColor Green

# 5. Run Verification Script 39 Only
Write-Host "`n[STEP 2/2] Running Script 39 (Verify FIFO Valuation State)..." -ForegroundColor Yellow
Write-Host "[SAFETY] Migration 026 and Backfill 38 are NOT executed." -ForegroundColor Green

$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $VerifySqlPath -Raw | & $psql "$psqlConn" -w -v ON_ERROR_STOP=1
} finally {
    $ErrorActionPreference = $prevEAP
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Verification Script 39 assertion failed! Check output above."
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== SUCCESS: Script 39 Operational Audit Verified on Neon neondb       ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
