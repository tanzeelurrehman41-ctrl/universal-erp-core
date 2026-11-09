<#
.SYNOPSIS
    Universal ERP -- Inspect & Grant Minimal Inventory Write Access (Product & UoM Creation)
.DESCRIPTION
    Safely inspects Neon database preconditions and idempotently grants 'inventory.product.create'
    and 'inventory.uom.create' to DEMO_INVENTORY_VIEWER for tanzeelurrehman41@gmail.com in DEMO-CLOUD.
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
$InspectSqlPath = Join-Path $ScriptDir "09_inspect_inventory_write_preconditions.sql"
$GrantSqlPath = Join-Path $ScriptDir "10_grant_demo_inventory_write.sql"

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
Write-Host "=== Universal ERP -- Controlled Inventory Write Permission Management  ===" -ForegroundColor Cyan
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

# Interactive Secure Prompt if still not resolved
if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Host "`n[SECURE PROMPT] No connection string found in parameters, environment, or secrets file." -ForegroundColor Yellow
    Write-Host "Please enter your Neon connection string below (input is masked and never logged):" -ForegroundColor Cyan
    try {
        $sec = Read-Host -Prompt "Neon Connection String" -AsSecureString
        if ($sec -ne $null) {
            $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
            $rawConn = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
        }
    } catch {
        $rawConn = ""
    }
}

if ([string]::IsNullOrWhiteSpace($rawConn)) {
    throw "Connection string is required to execute."
}

# 2. Strict Safety Guard: Block Local Production Targets
if ($rawConn -match "127\.0\.0\.1:5432" -or $rawConn -match "localhost:5432" -or $rawConn -match "universal_erp") {
    throw "SECURITY ABORT: Attempted execution against local production database (port 5432 or universal_erp). Script is strictly permitted against remote Neon cloud database only!"
}

# Convert ADO.NET / Npgsql format to standard PostgreSQL URI for psql
$psqlConn = Convert-ToPostgreSqlUri $rawConn

# Verify Hostname Belongs to Neon (*.neon.tech)
$targetHost = ""
if ($psqlConn -match "@([^:/]+)") {
    $targetHost = $Matches[1]
}

if ($targetHost -notmatch "\.neon\.tech$" -and $targetHost -ne "169.254.254.254") {
    throw "SECURITY ABORT: Host '$targetHost' does not belong to Neon cloud infrastructure (*.neon.tech). Target must be a Neon Free PostgreSQL instance."
}

# Ensure unpooled direct endpoint for transactional DDL / DML
if ($targetHost -match "-pooler\.") {
    $directHost = $targetHost -replace "-pooler\.", "."
    $psqlConn = $psqlConn -replace [regex]::Escape($targetHost), $directHost
    Write-Host "[ROUTING] Detected pooler endpoint. Using direct unpooled endpoint: $directHost" -ForegroundColor Green
}

Write-Host "Target Host : $targetHost" -ForegroundColor Cyan
Write-Host "Connection  : $(Mask-ConnectionString $psqlConn)" -ForegroundColor Cyan

# 3. Locate PSQL
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
if (-not (Test-Path $psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCmd) { $psql = $psqlCmd.Source } else { $psql = "psql" }
}

# 4. Step 1: Verify Remote Database Identity
Write-Host "`n[STEP 1/4] Verifying remote Neon database..." -ForegroundColor Yellow
$probeSql = "SELECT current_database() || '|' || current_user || '|' || version();"
$probeOutput = & $psql "$psqlConn" -w -t -A -c "$probeSql" 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to connect to Neon PostgreSQL. Error: $probeOutput"
}

$parts = $probeOutput.Trim() -split '\|'
$dbName = $parts[0]
$dbUser = $parts[1]
$pgVer = $parts[2]

Write-Host "Connected Database : $dbName" -ForegroundColor Green
Write-Host "Database User      : $dbUser" -ForegroundColor Green
Write-Host "Cluster Version    : $pgVer" -ForegroundColor Green

if ($dbName -ne "neondb") {
    throw "Target database name must be 'neondb'. Detected: $dbName"
}

# 5. Step 2: Execute Precondition Inspection
Write-Host "`n[STEP 2/4] Executing Read-Only Inventory Write Preconditions Inspection..." -ForegroundColor Yellow
if (-not (Test-Path $InspectSqlPath)) {
    throw "Inspection SQL file missing at $InspectSqlPath"
}

$inspectOutput = Get-Content $InspectSqlPath -Raw | & $psql "$psqlConn" -w 2>&1
Write-Host ($inspectOutput | Out-String) -ForegroundColor Cyan

if ($InspectOnly) {
    Write-Host "`n[INSPECT-ONLY] Precondition inspection complete. Exiting without modifying database." -ForegroundColor Yellow
    exit 0
}

# 6. Step 3: Execute Transactional Permission Grant
Write-Host "`n[STEP 3/4] Executing Transactional Permission Update (10_grant_demo_inventory_write.sql)..." -ForegroundColor Yellow
if (-not (Test-Path $GrantSqlPath)) {
    throw "Grant SQL file missing at $GrantSqlPath"
}

Get-Content $GrantSqlPath -Raw | & $psql "$psqlConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Transaction failed."
}
Write-Host "[PASS] Controlled write permission transaction committed successfully." -ForegroundColor Green

# 7. Step 4: Verification of user_has_permission
Write-Host "`n[STEP 4/4] Verifying updated live permissions for tanzeelurrehman41@gmail.com..." -ForegroundColor Yellow
$verifySql = @"
SELECT 
    p.permission_code,
    identity.user_has_permission(
        '22222222-2222-4222-8222-222222222222'::uuid,
        '22222222-2222-4222-8222-222222222223'::uuid,
        u.id,
        p.permission_code
    ) AS is_granted
FROM (
    VALUES 
        ('inventory.product.view'),
        ('inventory.uom.view'),
        ('inventory.variant.view'),
        ('inventory.warehouse.view'),
        ('inventory.availability.view'),
        ('inventory.product.create'),
        ('inventory.uom.create'),
        ('inventory.receipt.post'),
        ('inventory.adjustment.create')
) AS p(permission_code)
CROSS JOIN identity.users u
WHERE u.email_normalized = 'tanzeelurrehman41@gmail.com';
"@

$verifyOutput = & $psql "$psqlConn" -w -c "$verifySql"
$verifyOutputStr = ($verifyOutput | Out-String)
Write-Host $verifyOutputStr -ForegroundColor Cyan

if ($verifyOutputStr -match "inventory.product.view.*\|\s*t" -and
    $verifyOutputStr -match "inventory.uom.view.*\|\s*t" -and
    $verifyOutputStr -match "inventory.variant.view.*\|\s*t" -and
    $verifyOutputStr -match "inventory.warehouse.view.*\|\s*t" -and
    $verifyOutputStr -match "inventory.availability.view.*\|\s*t" -and
    $verifyOutputStr -match "inventory.product.create.*\|\s*t" -and
    $verifyOutputStr -match "inventory.uom.create.*\|\s*t" -and
    $verifyOutputStr -match "inventory.receipt.post.*\|\s*f" -and
    $verifyOutputStr -match "inventory.adjustment.create.*\|\s*f") {
    Write-Host "[PASS] Both product.create and uom.create granted; all 5 view permissions preserved; write/receipt denied." -ForegroundColor Green
} else {
    throw "VERIFICATION FAILED: user_has_permission check did not match expected least-privilege matrix!"
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== CONTROLLED INVENTORY WRITE ACCESS GRANTED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
Write-Host "The queued product 'scissor' ('idtem-02') will now sync cleanly on the frontend:" -ForegroundColor Yellow
Write-Host "https://universal-erp-demo.tanzeelurrehman41.workers.dev" -ForegroundColor Cyan
Write-Host "Expected Result: 'scissor' status transitions to 'accepted' with server product ID." -ForegroundColor Green
