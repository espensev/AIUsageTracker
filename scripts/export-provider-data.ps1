# Export real provider data from Monitor database for CI testing
# Run this locally after running the Monitor with real API keys to generate test fixtures

param(
    [string]$OutputPath = "test-fixtures/provider-data.json"
)

$ErrorActionPreference = "Stop"

$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$dbPath = Join-Path $appData "AIUsageTracker\usage.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath" -ForegroundColor Red
    Write-Host "Please run the Monitor first to generate real data." -ForegroundColor Yellow
    exit 1
}

Write-Host "Reading from: $dbPath" -ForegroundColor Cyan

# Delegate to the Seeder's export mode so the fixture contract (latest snapshot per
# provider and card, plus the 7-day window) has a single implementation. The inline
# SQL that used to live here grouped latest rows by provider only (merging cards)
# and compared epoch timestamps against datetime text, which exported nothing.
dotnet run --project "$PSScriptRoot/Seeder/Seeder.csproj" --no-build -- export "$OutputPath"
exit $LASTEXITCODE
