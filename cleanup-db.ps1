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

# Stop containers
Write-Host "⏹️  Stopping Docker containers..." -ForegroundColor Yellow
docker-compose down
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Containers stopped" -ForegroundColor Green
} else {
    Write-Host "⚠️  No containers were running" -ForegroundColor Yellow
}

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
Write-Host "  1. Run: docker-compose up -d" -ForegroundColor White
Write-Host "  2. Wait for containers to start" -ForegroundColor White
Write-Host "  3. Check logs: docker logs questions-hub-web" -ForegroundColor White
Write-Host "==================================" -ForegroundColor Cyan

