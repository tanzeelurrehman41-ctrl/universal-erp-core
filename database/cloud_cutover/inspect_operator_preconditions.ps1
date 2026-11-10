<#
.SYNOPSIS
    Universal ERP -- Inspect Inventory Permissions & Rollback State on Neon
.DESCRIPTION
    Safely executes read-only queries against Neon neondb to verify:
    1. Whether 'inventory.transfer.create' and 'inventory.adjustment.create' exist in authorization.permissions.
    2. Whether the failed transaction rolled back completely with 0 partial DEMO_INVENTORY_OPERATOR roles or assignments.
    3. That DEMO_INVENTORY_VIEWER retains its exact 11 permissions.
    4. That Zeelman's context remains unaffected.
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
$InspectSqlPath = Join-Path $ScriptDir "20_inspect_operator_preconditions.sql"

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
Write-Host "=== Universal ERP -- Read-Only Operator Preconditions Inspection       ===" -ForegroundColor Cyan
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
                $jsonContent = Get-Content $file -Raw | ConvertFrom-Json
                $val = $null
                if ($jsonContent.PSObject.Properties['NEON_MIGRATION_CONNECTION_STRING']) {
                    $val = $jsonContent.NEON_MIGRATION_CONNECTION_STRING
                } elseif ($jsonContent.PSObject.Properties['migration_connection_string']) {
                    $val = $jsonContent.migration_connection_string
                } elseif ($jsonContent.PSObject.Properties['connection_string']) {
                    $val = $jsonContent.connection_string
                }

                if (-not [string]::IsNullOrWhiteSpace($val)) {
                    Write-Host "[SECRETS] Loaded connection string from secret file: $file" -ForegroundColor Green
                    $rawConn = $val.ToString().Trim()
                    break
                }
            } catch {}
        }
    }
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Host "`n[SECURE PROMPT] No connection string found in parameters, environment, or secrets file." -ForegroundColor Yellow
    Write-Host "Please enter your Neon connection string below (input is masked and never logged):" -ForegroundColor Cyan
    try {
        $sec = Read-Host -Prompt "Neon Connection String" -AsSecureString
        if ($sec -ne $null) {
            $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
            $rawConn = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
    } catch {}
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Error "ABORT: A valid Neon connection string must be provided."
}

# 2. Strict Safety Validations
$masked = Mask-ConnectionString $rawConn
Write-Host "[TARGET DATABASE] $masked" -ForegroundColor Cyan

if ($rawConn -match '(?i)(localhost|127\.0\.0\.1|::1)') {
    Write-Error "CRITICAL SAFETY ABORT: Refusing to run against localhost/127.0.0.1! Must be Neon cloud database."
}

if ($rawConn -notmatch '(?i)neondb') {
    Write-Error "CRITICAL SAFETY ABORT: Target database is not 'neondb'! Aborting."
}

if ($rawConn -match '(?i)universal_erp') {
    Write-Error "CRITICAL SAFETY ABORT: Target connection string references local database 'universal_erp'! Aborting."
}

$psqlConn = Convert-ToPostgreSqlUri $rawConn

# 3. Locate psql binary
$psql = "psql"
$psqlCmd = Get-Command "psql" -ErrorAction SilentlyContinue
if (-not $psqlCmd) {
    $commonPsqlPaths = @(
        "C:\Program Files\PostgreSQL\17\bin\psql.exe",
        "C:\Program Files\PostgreSQL\16\bin\psql.exe",
        "C:\Program Files\PostgreSQL\15\bin\psql.exe",
        "C:\tools\postgresql\bin\psql.exe"
    )
    foreach ($p in $commonPsqlPaths) {
        if (Test-Path $p) {
            $psql = $p
            break
        }
    }
}

Write-Host "`n[STEP 1/2] Verifying remote Neon database..." -ForegroundColor Yellow
$verifySql = "SELECT current_database(), current_user, inet_server_addr(), version();"
$verifyOutput = $verifySql | & $psql "$psqlConn" -w 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Database connection failed:`n$verifyOutput"
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

Write-Host "`n[STEP 2/2] Running Read-Only Preconditions Inspection..." -ForegroundColor Yellow
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    Get-Content $InspectSqlPath -Raw | & $psql "$psqlConn" -w
} finally {
    $ErrorActionPreference = $prevEAP
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== Inspection Completed Successfully (Read-Only)                     ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
