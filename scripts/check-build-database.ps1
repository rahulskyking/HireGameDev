# Run from the repository root with PowerShell. No application database migration is applied.
# PostgreSQL integration tests create and drop hgd_test_<guid> databases on the server
# specified by HGD_TEST_POSTGRES. Use a disposable local server only.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'BLOCKED: .NET SDK not found. Install the .NET 8 SDK before running this check.' -ForegroundColor Red
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()
function Invoke-Check([string]$Label, [string[]]$Arguments) {
    Write-Host "`n========== $Label ==========" -ForegroundColor Cyan
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Label (exit $LASTEXITCODE)" -ForegroundColor Red
        $script:failures.Add($Label)
        return $false
    }
    Write-Host "COMPLETED: $Label" -ForegroundColor Green
    return $true
}

Write-Host 'Checking build, migration status and tests. This script does not apply migrations to your application database.'
Write-Host 'PostgreSQL tests create and drop temporary test databases: use only a disposable local PostgreSQL server.'

$built = Invoke-Check 'Build solution' @('build', 'HireGameDev.sln', '--nologo')
if (-not $built) {
    Write-Host 'Build failed. Migration and test commands were not run.' -ForegroundColor Red
    exit 1
}

$null = Invoke-Check 'Unit tests (excluding PostgreSQL integration)' @('test', 'tests/HireGameDev.UnitTests/HireGameDev.UnitTests.csproj', '--no-build', '--filter', 'Category!=PostgresIntegration', '--logger', 'console;verbosity=normal')
$toolReady = Invoke-Check 'Restore local EF tool' @('tool', 'restore')
if ($toolReady) {
    $null = Invoke-Check 'List application database migrations (read-only)' @('ef', 'migrations', 'list', '--project', 'src/HireGameDev.Infrastructure', '--startup-project', 'src/HireGameDev.Web')
} else {
    $failures.Add('Migration status not checked')
}

if ([string]::IsNullOrWhiteSpace($env:HGD_TEST_POSTGRES)) {
    Write-Host "`nBLOCKED: PostgreSQL integration tests not run; HGD_TEST_POSTGRES is not set." -ForegroundColor Yellow
    Write-Host 'Set it locally to a disposable PostgreSQL maintenance connection, then rerun. Do not paste credentials into chat.'
    $failures.Add('PostgreSQL integration tests not run')
} else {
    $null = Invoke-Check 'PostgreSQL integration tests (temporary test databases)' @('test', 'tests/HireGameDev.UnitTests/HireGameDev.UnitTests.csproj', '--no-build', '--filter', 'Category=PostgresIntegration', '--logger', 'console;verbosity=normal')
}

Write-Host "`n========== SUMMARY ==========" -ForegroundColor Cyan
if ($failures.Count -gt 0) {
    Write-Host 'Not all checks completed successfully:' -ForegroundColor Yellow
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host 'Commands completed. Review test totals and migration status above; skipped tests are not passes.' -ForegroundColor Green
Write-Host 'Existing integration tests do not cover all newer subscription, switch, assignment and search-quota paths.'
