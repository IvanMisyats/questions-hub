# Stop development database

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Stopping Development Environment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Stop database
Write-Host "Stopping PostgreSQL database..." -ForegroundColor Yellow
& "$PSScriptRoot\stop-dev-db.ps1"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Development environment stopped." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

