<#
.SYNOPSIS
    Universal ERP -- Apply Migration 026 and Model A FIFO Backfill on Neon
.DESCRIPTION
    Applies:
      1. Migration 026: 026_fifo_valuation_layers.sql (DDL for cost layers and allocations)
      2. Backfill 38: 38_propose_fifo_layer_backfill.sql (Model A physical chronological FIFO backfill)
      3. Verification 39: 39_verify_fifo_valuation_state.sql (Strict post-backfill validation)
    Zero modifications to historical invoices, stock movements, or posted journals.
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
$RepoRootDir = Split-Path -Parent (Split-Path -Parent $ScriptDir)

$MigrationSqlPath = Join-Path $RepoRootDir "database\migrations\026_fifo_valuation_layers.sql"
$BackfillSqlPath = Join-Path $ScriptDir "38_propose_fifo_layer_backfill.sql"
$VerifySqlPath = Join-Path $ScriptDir "39_verify_fifo_valuation_state.sql"

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
Write-Host "=== Universal ERP -- Apply Migration 026 & Model A FIFO Backfill (Neon)===" -ForegroundColor Cyan
Write-Host "==========================================================================" -ForegroundColor Cyan

# 1. Resolve Connection String
$rawConn = $NeonConnectionString

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    if (-not [string]::IsNullOrWhiteSpace($env:NEON_MIGRATION_CONNECTION_STRING)) {
        Write-Host "[SECRETS] Loaded connection string from environment variable NEON_MIGRATION_CONNECTION_STRING." -ForegroundColor Green
        $rawConn = $env:NEON_MIGRATION_CONNECTION_STRING.Trim()
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
            } catch {
                # continue searching
            }
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

# Prohibit local production
if ($psqlConn -match "localhost" -or $psqlConn -match "127\.0\.0\.1") {
    Write-Error "CRITICAL SAFETY VIOLATION: Execution against localhost / local production is strictly blocked!"
}

# Must contain neon.tech or neondb
if ($psqlConn -notmatch "neon\.tech" -and $psqlConn -notmatch "neondb") {
    Write-Error "CRITICAL SAFETY VIOLATION: The connection string does not appear to point to Neon cloud database!"
}

Write-Host "[TARGET DATABASE] $(Mask-ConnectionString $psqlConn)" -ForegroundColor Gray

# 3. Locate psql binary
$psql = Get-Command psql -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1

if (-not $psql) {
    $searchPaths = @(
        "C:\Program Files\PostgreSQL\*\bin\psql.exe",
        "C:\Program Files (x86)\PostgreSQL\*\bin\psql.exe",
        "$env:LOCALAPPDATA\Programs\PostgreSQL\*\bin\psql.exe"
    )
    foreach ($p in $searchPaths) {
        $found = Get-Item $p -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
        if ($found) {
            $psql = $found
            break
        }
    }
}

if (-not $psql) {
    Write-Error "psql command not found! Please ensure PostgreSQL client tools are installed and in PATH."
}

Write-Host "[PSQL CLIENT] $psql" -ForegroundColor Gray

# 4. Verify Remote Neon DB Connection
Write-Host "`n[STEP 1/4] Verifying remote Neon database..." -ForegroundColor Yellow
$verifySql = "SELECT current_database(), current_user, inet_server_addr(), version();"
$verifyOutput = & $psql "$psqlConn" -w -t -A -F "|" -c $verifySql 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to connect to remote database: $verifyOutput"
}

$dbName = ""
$dbUser = ""
$pgVer = ""
foreach ($line in ($verifyOutput -split "`n")) {
    $parts = $line.Trim().Split('|')
    if ($parts.Count -ge 3 -and $parts[0].Trim() -eq "neondb") {
        $dbName = "neondb"
        $dbUser = $parts[1].Trim()
        $pgVer = $parts[-1].Trim()
        break
    }
}

if ($dbName -ne "neondb") {
    Write-Error "Remote database assertion failed! Connected to: '$verifyOutput'"
}

if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

Write-Host "Connected Database : $dbName" -ForegroundColor Green
Write-Host "Database User      : $dbUser" -ForegroundColor Green
Write-Host "Cluster Version    : $pgVer" -ForegroundColor Green

# 5. Apply Migration 026
Write-Host "`n[STEP 2/4] Applying Migration 026 (FIFO Valuation Layers DDL)..." -ForegroundColor Yellow
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $MigrationSqlPath -Raw | & $psql "$psqlConn" -w -v ON_ERROR_STOP=1
} finally {
    $ErrorActionPreference = $prevEAP
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Migration 026 failed to apply! Check errors above."
}
Write-Host "[PASS] Migration 026 applied successfully." -ForegroundColor Green

# 6. Apply Model A FIFO Backfill
Write-Host "`n[STEP 3/4] Applying Script 38 (Model A Historical FIFO Backfill)..." -ForegroundColor Yellow
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $BackfillSqlPath -Raw | & $psql "$psqlConn" -w -v ON_ERROR_STOP=1
} finally {
    $ErrorActionPreference = $prevEAP
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Backfill Script 38 failed to apply! Check errors above."
}
Write-Host "[PASS] Script 38 executed successfully." -ForegroundColor Green

# 7. Run Verification Script 39
Write-Host "`n[STEP 4/4] Running Script 39 (Verify FIFO Valuation State)..." -ForegroundColor Yellow
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
Write-Host "=== SUCCESS: Migration 026 & Model A Backfill Verified on Neon        ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
