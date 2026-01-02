# Database Cleanup Script for Windows
# This script stops containers and removes the database folder

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Database Cleanup Script" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "docker-compose.yml")) {
    Write-Host "❌ Error: docker-compose.yml not found!" -ForegroundColor Red
    Write-Host "Please run this script from C:\Projects\questions-hub\" -ForegroundColor Yellow
    exit 1
}

# Stop containers (try both profiles)
Write-Host "⏹️  Stopping Docker containers..." -ForegroundColor Yellow
docker compose --profile dev down 2>$null
docker compose --profile production down 2>$null
Write-Host "✅ Containers stopped" -ForegroundColor Green

Write-Host ""

# Check if postgres_data exists
$dbPath = ".\postgres_data"
if (Test-Path $dbPath) {
    # Calculate size
    $size = (Get-ChildItem -Recurse $dbPath -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    $sizeMB = [math]::Round($size / 1MB, 2)

    Write-Host "📁 Found database folder: $dbPath" -ForegroundColor Cyan
    Write-Host "📊 Size: $sizeMB MB" -ForegroundColor Cyan
    Write-Host ""

    # Confirm deletion
    $confirm = Read-Host "⚠️  Delete database? This will erase ALL data! (yes/no)"

    if ($confirm -eq "yes") {
        Write-Host "🗑️  Deleting database folder..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force $dbPath -ErrorAction SilentlyContinue

        if (-not (Test-Path $dbPath)) {
            Write-Host "✅ Database deleted successfully!" -ForegroundColor Green
        } else {
            Write-Host "❌ Failed to delete database" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "❌ Cleanup cancelled" -ForegroundColor Yellow
        exit 0
    }
} else {
    Write-Host "✅ No database folder found (already clean)" -ForegroundColor Green
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Run: .\start-dev-db.ps1" -ForegroundColor White
Write-Host "  2. Wait for database to start" -ForegroundColor White
Write-Host "  3. Run the Blazor app from your IDE" -ForegroundColor White
Write-Host "==================================" -ForegroundColor Cyan

