[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$migrationDirectory = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $migrationDirectory "..\..")).Path
$composeFile = Join-Path $repoRoot "deploy\docker\docker-compose.yml"
$containerName = "universal-erp-postgres"

& docker compose -f $composeFile up -d postgres
if ($LASTEXITCODE -ne 0) {
    throw "Could not start the local PostgreSQL service."
}

$healthDeadline = (Get-Date).AddMinutes(2)
$healthStatus = "starting"
while ((Get-Date) -lt $healthDeadline) {
    $healthOutput = & docker inspect --format '{{.State.Health.Status}}' $containerName 2>$null
    if ($LASTEXITCODE -eq 0) {
        $healthStatus = $healthOutput.Trim()
    }
    if ($healthStatus -eq "healthy") {
        break
    }
    if ($healthStatus -eq "unhealthy") {
        throw "The local PostgreSQL service reported unhealthy."
    }
    Start-Sleep -Seconds 2
}
if ($healthStatus -ne "healthy") {
    throw "Timed out waiting for local PostgreSQL to become healthy."
}

& docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U erp_dev -d universal_erp -c "CREATE TABLE IF NOT EXISTS public.schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());"
if ($LASTEXITCODE -ne 0) {
    throw "Could not initialize migration history."
}

$migrationFiles = Get-ChildItem -LiteralPath $migrationDirectory -File -Filter "*.sql" |
    Where-Object { $_.BaseName -match '^\d{3}_[A-Za-z0-9_-]+$' } |
    Sort-Object Name

if ($migrationFiles.Count -eq 0) {
    throw "No numbered SQL migrations were found."
}

foreach ($migration in $migrationFiles) {
    $version = $migration.BaseName
    $alreadyApplied = (& docker exec $containerName psql -X -A -t -U erp_dev -d universal_erp -c "SELECT EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = '$version');").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check migration history for $version."
    }
    if ($alreadyApplied -eq "t") {
        Write-Host "Already applied: $version"
        continue
    }

    $migrationSql = Get-Content -LiteralPath $migration.FullName -Raw
    $wrappedSql = @"
BEGIN;
$migrationSql
INSERT INTO public.schema_migrations (version) VALUES ('$version');
COMMIT;
"@
    $temporaryName = "universal-erp-$version-$([guid]::NewGuid().ToString('N')).sql"
    $localTemporaryPath = Join-Path ([System.IO.Path]::GetTempPath()) $temporaryName
    $remoteTemporaryPath = "/tmp/$temporaryName"

    try {
        [System.IO.File]::WriteAllText(
            $localTemporaryPath,
            $wrappedSql,
            [System.Text.UTF8Encoding]::new($false))

        & docker cp $localTemporaryPath "${containerName}:$remoteTemporaryPath"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not stage migration $version in the database container."
        }

        Write-Host "Applying: $version"
        & docker exec $containerName psql -X -v ON_ERROR_STOP=1 -U erp_dev -d universal_erp -f $remoteTemporaryPath
        if ($LASTEXITCODE -ne 0) {
            throw "Migration $version failed. Its transaction was rolled back."
        }
    }
    finally {
        if (Test-Path -LiteralPath $localTemporaryPath) {
            Remove-Item -LiteralPath $localTemporaryPath
        }
        & docker exec $containerName rm -f -- $remoteTemporaryPath 2>$null | Out-Null
    }
}

Write-Host "All numbered migrations are applied."
