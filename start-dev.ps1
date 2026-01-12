# Start development database
# Use this for development environment

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Starting Development Environment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Start database
Write-Host "Starting PostgreSQL database..." -ForegroundColor Yellow
& "$PSScriptRoot\start-dev-db.ps1"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Development environment is ready!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Services running:" -ForegroundColor Gray
Write-Host "  - PostgreSQL: localhost:5432" -ForegroundColor Gray
Write-Host ""
Write-Host "Run the Blazor app from your IDE (F5)" -ForegroundColor Gray
Write-Host "To stop: .\stop-dev.ps1" -ForegroundColor Gray

