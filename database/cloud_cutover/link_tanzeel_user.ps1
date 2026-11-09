<#
.SYNOPSIS
    Universal ERP -- Inspect and Link Tanzeel Demo Account on Neon Cloud Database
.DESCRIPTION
    Safely inspects and links the staging Firebase account (tanzeelurrehman41@gmail.com, UID: nnnmCg7nT0cehV14o5APFFtY2s53)
    to the synthetic DEMO-CLOUD tenant and CLOUD-LOGISTICS company in Neon PostgreSQL.
    Reuses existing identity.users row if present, maintains least privilege, and preserves all existing memberships.
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
$InspectSqlPath = Join-Path $ScriptDir "05_inspect_tanzeel_user.sql"
$LinkSqlPath = Join-Path $ScriptDir "06_link_tanzeel_user.sql"

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

    # Parse semicolon-delimited key=value pairs (ADO.NET / Npgsql format)
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
Write-Host "=== Universal ERP -- Tanzeel Demo Account Linkage on Neon Cloud DB     ===" -ForegroundColor Cyan
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

# Ensure unpooled direct endpoint for DDL / transactional DML
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

# 5. Step 2: Read-Only Inspection
Write-Host "`n[STEP 2/4] Executing Read-Only Inspection for tanzeelurrehman41@gmail.com..." -ForegroundColor Yellow
if (-not (Test-Path $InspectSqlPath)) {
    throw "Inspection SQL file missing at $InspectSqlPath"
}

$inspectOutput = Get-Content $InspectSqlPath -Raw | & $psql "$psqlConn" -w 2>&1
Write-Host ($inspectOutput | Out-String) -ForegroundColor Cyan

if ($InspectOnly) {
    Write-Host "`n[INSPECT-ONLY] Read-only inspection complete. Exiting without modifying database." -ForegroundColor Yellow
    exit 0
}

# 6. Step 3: Execute Transactional Repair
Write-Host "`n[STEP 3/4] Executing transactional demo user linking for tanzeelurrehman41@gmail.com..." -ForegroundColor Yellow
if (-not (Test-Path $LinkSqlPath)) {
    throw "Linking SQL file missing at $LinkSqlPath"
}

Get-Content $LinkSqlPath -Raw | & $psql "$psqlConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Linking transaction failed."
}
Write-Host "[PASS] User linkage transaction committed successfully." -ForegroundColor Green

# 7. Step 4: Verification of list_user_company_contexts for both accounts
Write-Host "`n[STEP 4/4] Verifying workspace context listing across demo accounts..." -ForegroundColor Yellow
$verifyContextSql = @"
SELECT 
    ext.subject AS uid,
    u.email_normalized AS email,
    ctx.tenant_code,
    ctx.company_code,
    ctx.company_name,
    ctx.base_currency_code,
    ctx.timezone
FROM identity.external_identities ext
JOIN identity.users u ON u.id = ext.user_id
CROSS JOIN LATERAL identity.list_user_company_contexts(ext.issuer, ext.subject) ctx
WHERE ext.issuer = 'https://securetoken.google.com/universal-erp-staging'
  AND ext.subject IN ('nnnmCg7nT0cehV14o5APFFtY2s53', 'g5UVBoX1RFODIQ3suHZtN1DUiam2')
ORDER BY u.email_normalized;
"@

$verifyOutput = & $psql "$psqlConn" -w -c "$verifyContextSql"
$verifyOutputStr = ($verifyOutput | Out-String)
Write-Host $verifyOutputStr -ForegroundColor Cyan

# Validate Tanzeel context
if ($verifyOutputStr -match "tanzeelurrehman41@gmail.com" -and $verifyOutputStr -match "Cloud Global Logistics Ltd") {
    Write-Host "[PASS] Workspace lookup successfully verified for tanzeelurrehman41@gmail.com." -ForegroundColor Green
} else {
    throw "VERIFICATION FAILED: Workspace context lookup for tanzeelurrehman41@gmail.com did not return the expected company!"
}

# Validate Zeelman context invariant
if ($verifyOutputStr -match "zeelman49@gmail.com" -and $verifyOutputStr -match "Cloud Global Logistics Ltd") {
    Write-Host "[PASS] Preserved existing workspace lookup for zeelman49@gmail.com." -ForegroundColor Green
} else {
    throw "SAFETY VIOLATION: Existing workspace context lookup for zeelman49@gmail.com was lost!"
}

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== DEMO ACCOUNT LINKAGE COMPLETED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
Write-Host "The authenticated user (tanzeelurrehman41@gmail.com) can now refresh:" -ForegroundColor Yellow
Write-Host "https://universal-erp-demo.tanzeelurrehman41.workers.dev" -ForegroundColor Cyan
Write-Host "Expected Result: Workspace dropdown populated with 'Cloud Global Logistics Ltd (DEMO-CLOUD)'" -ForegroundColor Green
