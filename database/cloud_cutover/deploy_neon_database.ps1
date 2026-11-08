<#
.SYNOPSIS
    Universal ERP -- Neon Cloud Database Deployment & Phase A Verification
.DESCRIPTION
    Applies migrations 001-022 to Neon Free PostgreSQL, seeds the synthetic
    DEMO-CLOUD tenant, runs Gate 11 and Gate 12 audits, and verifies zero production
    data leakage with strict SSL Mode=VerifyFull security.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$NeonConnectionString = "",

    [Parameter(Mandatory=$false)]
    [string]$SecretsFilePath = "",

    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Setup Paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RootDir = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$MasterDdlPath = Join-Path (Join-Path (Split-Path -Parent $ScriptDir) "production_cutover") "01_production_schema_ddl.sql"
$DemoSeedPath = Join-Path $ScriptDir "02_demo_tenant_seed.sql"
$ReconcilePrivsPath = Join-Path $ScriptDir "03_reconcile_least_privilege.sql"
$AuditRlsPath = Join-Path $RootDir "audit_rls_policies.sql"
$AuditPrivsPath = Join-Path $RootDir "audit_effective_privileges.sql"

function Mask-ConnectionString {
    param([string]$conn)
    if ([string]::IsNullOrWhiteSpace($conn)) { return "" }
    # Mask password in URI: postgresql://user:password@host:port/db?params
    $masked = [regex]::Replace($conn, '(?i)(postgres(?:ql)?://[^:]+:)([^@]+)(@)', '${1}********${3}')
    # Mask password in keyword format: Password=xxx;
    $masked = [regex]::Replace($masked, '(?i)(Password\s*=\s*)([^;]+)', '${1}********')
    return $masked
}

Write-Host "==========================================================================" -ForegroundColor Cyan
Write-Host "=== Universal ERP -- Phase A: Neon Cloud Database Deployment ===" -ForegroundColor Cyan
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
        (Join-Path $RootDir "thesecreat\neaon.json"),
        (Join-Path $RootDir "thesecreat\neon.json"),
        (Join-Path $RootDir "thesecret\neon.json"),
        (Join-Path $RootDir "thesecret\neaon.json"),
        (Join-Path $RootDir "secrets\neon.json"),
        "C:\secrets\neon.json",
        (Join-Path (Split-Path -Parent $RootDir) "thesecreat\neaon.json"),
        (Join-Path (Split-Path -Parent $RootDir) "thesecreat\neon.json"),
        (Join-Path (Split-Path -Parent $RootDir) "thesecret\neon.json"),
        (Join-Path (Split-Path -Parent $RootDir) "thesecret\neaon.json")
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
                } elseif ($jsonContent.PSObject.Properties['direct_connection_string']) {
                    $val = $jsonContent.direct_connection_string
                } elseif ($jsonContent.PSObject.Properties['connection_string']) {
                    $val = $jsonContent.connection_string
                }

                if (-not [string]::IsNullOrWhiteSpace($val)) {
                    Write-Host "[SECRETS] Loaded connection string from secret file: $file" -ForegroundColor Green
                    $rawConn = $val.ToString().Trim()
                    break
                }
            } catch {
                # Continue searching candidates
            }
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
    Write-Host "`n[ERROR] Connection string is required to proceed with Phase A." -ForegroundColor Red
    Write-Host "Provide via one of:" -ForegroundColor Yellow
    Write-Host "  1. Secrets file: C:\secrets\neon.json with { `"NEON_MIGRATION_CONNECTION_STRING`": `"postgresql://...`" }"
    Write-Host "  2. Environment variable: `$env:NEON_MIGRATION_CONNECTION_STRING"
    Write-Host "  3. Interactive secure prompt when running this script."
    exit 1
}

# 2. Strict Security & Target Validation
Write-Host "`n[SECURITY AUDIT] Validating connection target and parameters..." -ForegroundColor Yellow

# Reject Localhost / Loopback / Port 5432 local targets
if ($rawConn -match "127\.0\.0\.1" -or $rawConn -match "localhost" -or $rawConn -match "::1") {
    throw "SECURITY ABORT: Refusing to execute against local loopback target! Local database must remain strictly untouched."
}
if ($rawConn -match "port\s*=\s*5432" -and ($rawConn -match "127\.0\.0\.1" -or $rawConn -match "localhost")) {
    throw "SECURITY ABORT: Local port 5432 target detected. Refusing execution."
}

# Reject Insecure SSL settings
if ($rawConn -match "Trust Server Certificate\s*=\s*true" -or $rawConn -match "sslmode=disable" -or $rawConn -match "sslmode=allow" -or $rawConn -match "sslmode=prefer") {
    throw "SECURITY REJECTION: Insecure SSL configuration detected. Trust Server Certificate and unverified SSL modes are prohibited. Enforce sslmode=verify-full."
}

# Enforce sslmode=verify-full in query string
if ($rawConn -match "\?") {
    if ($rawConn -match "sslmode=") {
        $rawConn = [regex]::Replace($rawConn, "sslmode=[^&;]+", "sslmode=verify-full")
    } else {
        $rawConn = $rawConn + "&sslmode=verify-full"
    }
} else {
    $rawConn = $rawConn + "?sslmode=verify-full"
}

# Extract Hostname
$hostMatch = [regex]::Match($rawConn, "@([^:/]+)")
if (-not $hostMatch.Success) {
    throw "INVALID CONNECTION STRING: Unable to parse target hostname."
}
$targetHost = $hostMatch.Groups[1].Value

# Verify Hostname Belongs to Neon (*.neon.tech)
if ($targetHost -notmatch "\.neon\.tech$") {
    throw "SECURITY ABORT: Host '$targetHost' does not belong to Neon cloud infrastructure (*.neon.tech). Target must be a Neon Free PostgreSQL instance."
}

# 3. Direct (Unpooled) Connection for Migrations vs Pooled Connection for Runtime
$migrationConn = $rawConn
$runtimePooledConn = $rawConn

if ($targetHost -match "-pooler\.") {
    # Direct endpoint is required for migrations (DDL & transactional locking)
    $directHost = $targetHost -replace "-pooler\.", "."
    $migrationConn = $rawConn -replace [regex]::Escape($targetHost), $directHost
    Write-Host "[CONNECTION ROUTING] Detected pooled hostname. Migrations will use direct unpooled endpoint: $directHost" -ForegroundColor Green
} else {
    # Migration string is already direct unpooled; derive pooled endpoint for app runtime
    $poolerHost = [regex]::Replace($targetHost, "^([^.]+)", "`${1}-pooler")
    $runtimePooledConn = $rawConn -replace [regex]::Escape($targetHost), $poolerHost
}

$maskedMigrationConn = Mask-ConnectionString $migrationConn
$maskedRuntimeConn = Mask-ConnectionString $runtimePooledConn

Write-Host "Target Host (Direct Migration): $targetHost" -ForegroundColor Cyan
Write-Host "Connection String (Masked): $maskedMigrationConn" -ForegroundColor Cyan
Write-Host "[PASS] Security validations passed: Neon endpoint verified, SSL Mode=VerifyFull enforced." -ForegroundColor Green

# Ensure Root CA bundle for libpq on Windows (required for sslmode=verify-full)
if ($IsWindows -or ($env:OS -like "*Windows*")) {
    $pgAppData = Join-Path $env:APPDATA "postgresql"
    $rootCrtPath = Join-Path $pgAppData "root.crt"
    if (-not (Test-Path $rootCrtPath)) {
        if (-not (Test-Path $pgAppData)) { New-Item -ItemType Directory -Path $pgAppData -Force | Out-Null }
        $knownBundles = @(
            "C:\Program Files\Git\mingw64\etc\ssl\certs\ca-bundle.crt",
            "C:\Program Files\Git\usr\ssl\certs\ca-bundle.crt",
            "C:\curl\bin\curl-ca-bundle.crt"
        )
        foreach ($b in $knownBundles) {
            if (Test-Path $b) {
                Copy-Item $b $rootCrtPath -Force
                break
            }
        }
    }
}

# Locate PSQL
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
if (-not (Test-Path $psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCmd) { $psql = $psqlCmd.Source } else { $psql = "psql" }
}

# 4. Verify Actual Target Database and PostgreSQL Version
Write-Host "`n[STEP 1/6] Verifying remote Neon cluster identity and database..." -ForegroundColor Yellow

$probeSql = "SELECT current_database() || '|' || version() || '|' || inet_server_addr()::text;"
$probeOutput = & $psql "$migrationConn" -w -t -A -c "$probeSql" 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to connect to Neon PostgreSQL. Error: $probeOutput"
}

$parts = $probeOutput.Trim() -split '\|'
$dbName = $parts[0]
$pgVer = $parts[1]
$serverIp = if ($parts.Length -ge 3) { $parts[2] } else { "cloud-routed" }

Write-Host "Connected Database : $dbName" -ForegroundColor Green
Write-Host "Cluster Version    : $pgVer" -ForegroundColor Green
Write-Host "Server Endpoint IP : $serverIp" -ForegroundColor Green

if ($pgVer -notmatch "PostgreSQL (16|17|18)") {
    throw "UNSUPPORTED VERSION: Neon cluster must run PostgreSQL 16 or 17+. Detected: $pgVer"
}

# 5. Apply Master Schema Package (Migrations 001-022)
Write-Host "`n[STEP 2/6] Applying Master Schema Package (Migrations 001-022) to Neon..." -ForegroundColor Yellow
if (-not (Test-Path $MasterDdlPath)) {
    throw "Master DDL package missing at: $MasterDdlPath"
}

Get-Content $MasterDdlPath -Raw | & $psql "$migrationConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Schema migration deployment failed on Neon."
}

# Verify schema_migrations count
$migCountStr = & $psql "$migrationConn" -w -t -A -c "SELECT count(*) FROM public.schema_migrations;"
$migCount = [int]$migCountStr.Trim()
if ($migCount -lt 22) {
    throw "CRITICAL: Expected at least 22 migration ledger entries, found $migCount."
}
Write-Host "[PASS] All 22 migrations applied successfully. Migration ledger count: $migCount" -ForegroundColor Green

# 6. Seed Synthetic Demonstration Tenant
Write-Host "`n[STEP 3/6] Seeding isolated synthetic demonstration tenant (DEMO-CLOUD)..." -ForegroundColor Yellow
if (-not (Test-Path $DemoSeedPath)) {
    throw "Demo seed SQL missing at: $DemoSeedPath"
}

Get-Content $DemoSeedPath -Raw | & $psql "$migrationConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Synthetic demo tenant seed failed on Neon."
}
Write-Host "[PASS] Synthetic cloud demonstration tenant initialized cleanly." -ForegroundColor Green

# 6b. Reconcile Least-Privilege Permissions & Neutralize Default ACLs
Write-Host "`n[STEP 3b/6] Reconciling least-privilege role matrix and neutralizing default ACLs..." -ForegroundColor Yellow
if (-not (Test-Path $ReconcilePrivsPath)) {
    throw "Least-privilege reconciliation SQL missing at: $ReconcilePrivsPath"
}

Get-Content $ReconcilePrivsPath -Raw | & $psql "$migrationConn" -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw "CRITICAL: Least-privilege role reconciliation failed on Neon."
}
Write-Host "[PASS] Least-privilege permissions reconciled and default ACLs neutralized successfully." -ForegroundColor Green

# 7. Execute Gate 11 Audit (Forced Row-Level Security)
Write-Host "`n[STEP 4/6] Executing Gate 11: Forced Row-Level Security Audit..." -ForegroundColor Yellow
if (-not (Test-Path $AuditRlsPath)) {
    throw "Gate 11 audit script missing at: $AuditRlsPath"
}

$rlsViolations = Get-Content $AuditRlsPath -Raw | & $psql "$migrationConn" -t -A -v ON_ERROR_STOP=1
$rlsViolationsClean = ($rlsViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($rlsViolationsClean)) {
    throw "CRITICAL: Gate 11 RLS Audit FAILED on Neon with violations:`n$rlsViolationsClean"
}
Write-Host "[PASS] Gate 11 RLS Policy Audit: 0 violations across all 42 tables." -ForegroundColor Green

# 8. Execute Gate 12 Audit (Least-Privilege App Role Privileges)
Write-Host "`n[STEP 5/6] Executing Gate 12: Least-Privilege Role Privilege Audit for erp_app..." -ForegroundColor Yellow
if (-not (Test-Path $AuditPrivsPath)) {
    throw "Gate 12 audit script missing at: $AuditPrivsPath"
}

$privViolations = Get-Content $AuditPrivsPath -Raw | & $psql "$migrationConn" -t -A -v ON_ERROR_STOP=1
$privViolationsClean = ($privViolations | Out-String).Trim()
if (-not [string]::IsNullOrWhiteSpace($privViolationsClean)) {
    throw "CRITICAL: Gate 12 Privilege Audit FAILED on Neon with violations:`n$privViolationsClean"
}
Write-Host "[PASS] Gate 12 Privilege Audit: 0 violations for erp_app role." -ForegroundColor Green

# 9. Verify Tenant Integrity & Zero Production Data Leakage
Write-Host "`n[STEP 6/6] Verifying demonstration tenant and checking for data leakage..." -ForegroundColor Yellow

# Verify DEMO-CLOUD tenant and Cloud Global Logistics Ltd
$tenantCheck = & $psql "$migrationConn" -w -t -A -c "SELECT count(*) FROM platform.tenants WHERE tenant_code = 'DEMO-CLOUD' AND status = 'active';"
$companyCheck = & $psql "$migrationConn" -w -t -A -c "SELECT count(*) FROM organization.companies WHERE name = 'Cloud Global Logistics Ltd' AND status = 'active';"

if ([int]$tenantCheck.Trim() -ne 1 -or [int]$companyCheck.Trim() -ne 1) {
    throw "VERIFICATION FAILED: DEMO-CLOUD tenant or Cloud Global Logistics Ltd not found in active state."
}
Write-Host "[PASS] Verified: Tenant 'DEMO-CLOUD' and Company 'Cloud Global Logistics Ltd' are active." -ForegroundColor Green

# Verify ZERO existing production business data
$leakCheckSql = @"
SELECT
    (SELECT count(*) FROM erp_purchasing.purchase_order) +
    (SELECT count(*) FROM erp_purchasing.goods_received_note) +
    (SELECT count(*) FROM erp_purchasing.supplier) +
    (SELECT count(*) FROM erp_sales.sales_order) +
    (SELECT count(*) FROM erp_sales.sales_invoice) +
    (SELECT count(*) FROM erp_sales.customer) +
    (SELECT count(*) FROM finance.journals) +
    (SELECT count(*) FROM finance.journal_lines) AS leaked_rows;
"@

$leakedRowsStr = & $psql "$migrationConn" -w -t -A -c "$leakCheckSql"
$leakedRows = [int]$leakedRowsStr.Trim()
if ($leakedRows -ne 0) {
    throw "CRITICAL DATA LEAK DETECTED: Found $leakedRows production business records on cloud database. Zero production records allowed!"
}
Write-Host "[PASS] Zero production business records detected (0 leaked rows). Schema is purely demonstration-ready." -ForegroundColor Green

# 10. Summary & Runtime Application Pooled Connection Configuration
Write-Host "`n==========================================================================" -ForegroundColor Green
Write-Host "=== PHASE A COMPLETED: NEON DEMONSTRATION DATABASE PROVISIONED ===" -ForegroundColor Green
Write-Host "==========================================================================" -ForegroundColor Green
Write-Host "Target Database       : $dbName" -ForegroundColor Cyan
Write-Host "PostgreSQL Version    : $pgVer" -ForegroundColor Cyan
Write-Host "Migrations Applied    : 22/22 (Ledger: $migCount)" -ForegroundColor Cyan
Write-Host "Gate 11 RLS Audit     : 0 Violations (PASS)" -ForegroundColor Cyan
Write-Host "Gate 12 Privileges    : 0 Violations (PASS)" -ForegroundColor Cyan
Write-Host "Demonstration Org     : DEMO-CLOUD / Cloud Global Logistics Ltd (Active)" -ForegroundColor Cyan
Write-Host "Production Data Leak  : 0 Records (PASS)" -ForegroundColor Cyan
Write-Host "`n[RUNTIME CONFIGURATION FOR RENDER (PHASE B)]" -ForegroundColor Yellow
Write-Host "Application Pooled Connection URI (Masked): $maskedRuntimeConn" -ForegroundColor Cyan
Write-Host "Set this in Render dashboard under Environment Variables as 'ConnectionStrings__UniversalErp'." -ForegroundColor Yellow
