# deploy_cloudflare_worker.ps1
# Deploys Universal ERP frontend to Cloudflare Worker (universal-erp-demo) with static assets
# Zero-exposure: Sensitive API tokens are handled in memory as secure strings and never logged.

[CmdletBinding()]
param(
    [string]$TargetUrl = "https://universal-erp-demo.tanzeelurrehman41.workers.dev"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$webDir = Join-Path $repoRoot "src\web"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " UNIVERSAL ERP: CLOUDFLARE WORKER DEPLOYMENT RUNNER" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. Verify required source files exist
Write-Host "`n[STEP 1/5] Verifying deployment configuration in src/web..." -ForegroundColor Yellow
$wranglerToml = Join-Path $webDir "wrangler.toml"
$workerTs = Join-Path $webDir "worker.ts"
$envProd = Join-Path $webDir ".env.production"

if (-not (Test-Path $wranglerToml)) { throw "Missing wrangler.toml at '$wranglerToml'!" }
if (-not (Test-Path $workerTs)) { throw "Missing worker.ts at '$workerTs'!" }
if (-not (Test-Path $envProd)) { throw "Missing .env.production at '$envProd'!" }

Write-Host "[PASS] All Cloudflare deployment files present." -ForegroundColor Green

# 2. Check Wrangler Authentication
Write-Host "`n[STEP 2/5] Checking Cloudflare authentication..." -ForegroundColor Yellow
$isAuthenticated = $false

if (-not [string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN)) {
    Write-Host "[INFO] Detected CLOUDFLARE_API_TOKEN in environment." -ForegroundColor Green
    $isAuthenticated = $true
} else {
    Push-Location $webDir
    try {
        $whoami = npx wrangler whoami 2>&1 | Out-String
        if ($whoami -match "logged in|authenticated as") {
            Write-Host "[PASS] Already authenticated via Wrangler OAuth." -ForegroundColor Green
            $isAuthenticated = $true
        }
    } catch {}
    finally {
        Pop-Location
    }
}

if (-not $isAuthenticated) {
    Write-Host "`n[AUTH REQUIRED] Wrangler is not currently authenticated." -ForegroundColor Yellow
    Write-Host "Options:"
    Write-Host "  1. Enter Cloudflare API Token securely (Create token with 'All Workers:Edit' permission)"
    Write-Host "  2. Perform interactive browser login via 'npx wrangler login'"
    
    $choice = Read-Host "`nEnter '1' to enter API Token, or '2' to launch browser login [1]"
    if ($choice -eq "2") {
        Write-Host "Launching interactive browser login..." -ForegroundColor Cyan
        Push-Location $webDir
        try {
            npx wrangler login
        } finally {
            Pop-Location
        }
    } else {
        $secureToken = Read-Host -Prompt "Enter CLOUDFLARE_API_TOKEN (input hidden)" -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
        $token = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

        if ([string]::IsNullOrWhiteSpace($token)) {
            throw "Aborted: Empty API token provided."
        }
        $env:CLOUDFLARE_API_TOKEN = $token
        Write-Host "[PASS] In-memory Cloudflare API Token set." -ForegroundColor Green
    }
}

# 3. Production Build
Write-Host "`n[STEP 3/5] Running production build in src/web..." -ForegroundColor Yellow
Push-Location $webDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with code $LASTEXITCODE." }
    Write-Host "[PASS] Production build completed successfully." -ForegroundColor Green

    # 4. Deploying to Cloudflare Worker
    Write-Host "`n[STEP 4/5] Deploying static assets to Cloudflare Worker..." -ForegroundColor Yellow
    npx wrangler deploy
    if ($LASTEXITCODE -ne 0) { throw "Wrangler deploy failed with code $LASTEXITCODE." }
    Write-Host "[PASS] Cloudflare Worker deployment succeeded." -ForegroundColor Green
} finally {
    Pop-Location
    # Clear in-memory token from environment if it was set interactively
    if ($choice -ne "2" -and -not [string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN)) {
        Remove-Item Env:\CLOUDFLARE_API_TOKEN -ErrorAction SilentlyContinue
    }
}

# 5. Live Verification
Write-Host "`n[STEP 5/5] Verifying live endpoint: $TargetUrl..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

try {
    $resp = Invoke-WebRequest -Uri $TargetUrl -UseBasicParsing
    if ($resp.StatusCode -ne 200) {
        throw "Target URL returned HTTP $($resp.StatusCode)."
    }

    if ($resp.Content.Trim() -eq "Hello world") {
        Write-Host "[FAIL] Endpoint is still returning 'Hello world'!" -ForegroundColor Red
        exit 1
    }

    if ($resp.Content -match "<div id=`"root`">|assets/index-") {
        Write-Host "[SUCCESS] Verified: Universal ERP React application is live at $TargetUrl" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "[WARNING] Received HTTP 200, but response did not contain expected React markup." -ForegroundColor Yellow
        Write-Host "Response preview: $($resp.Content.Substring(0, [Math]::Min(300, $resp.Content.Length)))"
    }
} catch {
    Write-Host "[FAIL] Live verification probe failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
