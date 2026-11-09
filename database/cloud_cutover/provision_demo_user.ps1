<#
.SYNOPSIS
    Universal ERP -- Provision Staging Demo User Accounts on Neon Cloud Database
.DESCRIPTION
    Safely and idempotently links staging Firebase authenticated accounts to
    the synthetic DEMO-CLOUD tenant and CLOUD-LOGISTICS company in Neon PostgreSQL.
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
$SqlPath = Join-Path $ScriptDir "04_provision_demo_user.sql"

Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host "=== Universal ERP -- Staging Demo Account Provisioning on Neon ===" -ForegroundColor Cyan
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
    throw "Connection string is required to execute provisioning."
}

# 2. Strict Safety Guard: Block Local Production Targets
if ($rawConn -match "127\.0\.0\.1:5432" -or $rawConn -match "localhost:5432" -or $rawConn -match "universal_erp") {
    throw "SECURITY ABORT: Attempted execution against local production database (port 5432 or universal_erp). Provisioning is strictly permitted against remote Neon cloud database only!"
}

# Verify Hostname Belongs to Neon (*.neon.tech)
$targetHost = ""
if ($rawConn -match "@([^:/]+)") {
    $targetHost = $Matches[1]
} elseif ($rawConn -match "(?i)Host\s*=\s*([^;]+)") {
    $targetHost = $Matches[1].Trim()
}

if ($targetHost -notmatch "\.neon\.tech$" -and $targetHost -ne "169.254.254.254") {
    throw "SECURITY ABORT: Host '$targetHost' does not belong to Neon cloud infrastructure (*.neon.tech). Target must be a Neon Free PostgreSQL instance."
}

# 3. Locate PSQL
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
if (-not (Test-Path $psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCmd) { $psql = $psqlCmd.Source } else { $psql = "psql" }
}

# 4. Verify Remote Database Identity
Write-Host "`n[STEP 1/4] Verifying remote Neon database..." -ForegroundColor Yellow
$probeSql = "SELECT current_database() || '|' || current_user || '|' || version();"
$probeOutput = & $psql "$rawConn" -w -t -A -c "$probeSql" 2>&1
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

# 5. Verify DEMO-CLOUD Tenant and CLOUD-LOGISTICS Company
Write-Host "`n[STEP 2/4] Verifying existing DEMO-CLOUD tenant and CLOUD-LOGISTICS company..." -ForegroundColor Yellow
$verifyOrgSql = @"
SELECT count(*) FROM platform.tenants t
JOIN organization.companies c ON c.tenant_id = t.id
WHERE t.id = '22222222-2222-4222-8222-222222222222'::uuid
  AND t.tenant_code = 'DEMO-CLOUD'
  AND t.status = 'active'
  AND c.id = '22222222-2222-4222-8222-222222222223'::uuid
  AND c.company_code = 'CLOUD-LOGISTICS'
  AND c.status = 'active';
"@

$orgCount = & $psql "$rawConn" -w -t -A -c "$verifyOrgSql"
if ([int]$orgCount.Trim() -ne 1) {
    throw "VERIFICATION FAILED: DEMO-CLOUD tenant or CLOUD-LOGISTICS company not found with expected IDs!"
}
Write-Host "[PASS] DEMO-CLOUD tenant and CLOUD-LOGISTICS company verified active with exact IDs." -ForegroundColor Green

# 6. Execute Provisioning Transaction
Write-Host "`n[STEP 3/4] Executing transactional demo user provisioning..." -ForegroundColor Yellow
if (-not (Test-Path $SqlPath)) {
    throw "Provisioning SQL file missing at $SqlPath"
}

Get-Content $SqlPath -Raw | & $psql "$rawConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Provisioning transaction failed."
}
Write-Host "[PASS] Staging demo user accounts provisioned successfully." -ForegroundColor Green

# 7. Verification of list_user_company_contexts for each account
Write-Host "`n[STEP 4/4] Verifying workspace context listing across provisioned accounts..." -ForegroundColor Yellow
$verifyContextSql = @"
SELECT 
    ext.subject AS uid,
    u.email_normalized AS email,
    ctx.tenant_code,
    ctx.company_name,
    ctx.base_currency_code
FROM identity.external_identities ext
JOIN identity.users u ON u.id = ext.user_id
CROSS JOIN LATERAL identity.list_user_company_contexts(ext.issuer, ext.subject) ctx
WHERE ext.issuer = 'https://securetoken.google.com/universal-erp-staging'
ORDER BY u.email_normalized;
"@

$verifyOutput = & $psql "$rawConn" -w -c "$verifyContextSql"
Write-Host $verifyOutput -ForegroundColor Cyan

Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== DEMO ACCOUNT PROVISIONING COMPLETED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
Write-Host "The authenticated user can now refresh the Cloudflare frontend:" -ForegroundColor Yellow
Write-Host "https://universal-erp-demo.tanzeelurrehman41.workers.dev" -ForegroundColor Cyan
Write-Host "Expected Result: Workspace dropdown populated with 'Cloud Global Logistics Ltd (DEMO-CLOUD)'" -ForegroundColor Green
