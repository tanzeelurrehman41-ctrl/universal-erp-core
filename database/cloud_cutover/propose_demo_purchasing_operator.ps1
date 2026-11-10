<#
.SYNOPSIS
    Universal ERP -- Inspect & Grant DEMO_PURCHASING_OPERATOR Role on Neon
.DESCRIPTION
    Safely inspects Neon neondb preconditions and idempotently creates the separate
    least-privilege role DEMO_PURCHASING_OPERATOR for Tanzeel Rehman in tenant DEMO-CLOUD.
    Grants strictly the 7 purchasing permissions (company scope):
      - purchasing.supplier.view
      - purchasing.supplier.create
      - purchasing.po.view
      - purchasing.po.create
      - purchasing.po.confirm
      - purchasing.grn.view
      - purchasing.grn.post
    Leaves DEMO_INVENTORY_VIEWER and DEMO_INVENTORY_OPERATOR roles completely untouched.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$NeonConnectionString = "",

    [Parameter(Mandatory=$false)]
    [string]$SecretsFilePath = "",

    [Parameter(Mandatory=$false)]
    [switch]$InspectOnly = $false
)

$ErrorActionPreference = "Stop"

# Setup Paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$GrantSqlPath = Join-Path $ScriptDir "22_propose_demo_purchasing_operator.sql"
$InspectSqlPath = Join-Path $ScriptDir "23_inspect_purchasing_preconditions.sql"

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
Write-Host "=== Universal ERP -- Propose DEMO_PURCHASING_OPERATOR Role             ===" -ForegroundColor Cyan
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
Write-Host "`n[STEP 1/3] Verifying remote Neon database..." -ForegroundColor Yellow
$verifySql = "SELECT current_database(), current_user, version();"
$verifyOutput = & $psql "$psqlConn" -w -t -A -F "|" -c $verifySql 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to connect to remote database: $verifyOutput"
}

$dbName = ""
$dbUser = ""
$pgVer = ""
foreach ($line in ($verifyOutput -split "`n")) {
    $trimmed = $line.Trim()
    if ($trimmed -match '^neondb\s*\|\s*([^|]+)\s*\|\s*([^|]+)\s*\|\s*(.+)$') {
        $dbName = "neondb"
        $dbUser = $Matches[1].Trim()
        $pgVer = $Matches[3].Trim()
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

if ($InspectOnly) {
    Write-Host "`n[STEP 2/2] Running Read-Only Preconditions Inspection..." -ForegroundColor Yellow
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        Get-Content $InspectSqlPath -Raw | & $psql "$psqlConn" -w
    } finally {
        $ErrorActionPreference = $prevEAP
    }
    Write-Host "`n[INSPECT FINISHED] Preconditions inspected safely." -ForegroundColor Green
    return
}

Write-Host "`n[STEP 2/3] Executing DEMO_PURCHASING_OPERATOR Grant Script..." -ForegroundColor Yellow
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $GrantSqlPath -Raw | & $psql "$psqlConn" -w -v ON_ERROR_STOP=1
} finally {
    $ErrorActionPreference = $prevEAP
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Role grant failed! Transaction was aborted and rolled back by PostgreSQL."
}

Write-Host "`n[STEP 3/3] Running Post-Grant Verification Inspection..." -ForegroundColor Yellow
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $InspectSqlPath -Raw | & $psql "$psqlConn" -w
} finally {
    $ErrorActionPreference = $prevEAP
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== SUCCESS: DEMO_PURCHASING_OPERATOR Granted & Validated on Neon      ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
