#!/usr/bin/env pwsh
# Start PostgreSQL for local development
# The Blazor app will run outside Docker and connect to this containerized database

Write-Host "Starting PostgreSQL for local development..." -ForegroundColor Green
Write-Host "Connection details:" -ForegroundColor Cyan
Write-Host "  Host: localhost" -ForegroundColor White
Write-Host "  Port: 5432" -ForegroundColor White
Write-Host "  Database: questionshub" -ForegroundColor White
Write-Host "  Username: questionshub" -ForegroundColor White
Write-Host "  Password: dev_password_123" -ForegroundColor White
Write-Host ""

# Set environment variables for local development
$env:POSTGRES_HOST_AUTH_METHOD = "trust"
$env:POSTGRES_ROOT_PASSWORD = "dev_root_password"
$env:QUESTIONSHUB_PASSWORD = "dev_password_123"

docker compose --profile dev up -d

Write-Host ""
Write-Host "PostgreSQL is starting..." -ForegroundColor Green
Write-Host "Waiting for database to be ready..." -ForegroundColor Yellow

# Wait for the database to be healthy
$maxAttempts = 30
$attempt = 0
$healthy = $false

while ($attempt -lt $maxAttempts -and -not $healthy) {
    $attempt++
    Start-Sleep -Seconds 1

    $status = docker inspect --format='{{.State.Health.Status}}' questions-hub-db 2>$null
    if ($status -eq "healthy") {
        $healthy = $true
        Write-Host ""
        Write-Host "Database is ready!" -ForegroundColor Green
        Write-Host ""
        Write-Host "You can now run the Blazor app locally from your IDE" -ForegroundColor Cyan
        Write-Host "To stop: .\stop-dev-db.ps1" -ForegroundColor Yellow
    } else {
        Write-Host "." -NoNewline -ForegroundColor Yellow
    }
}

if (-not $healthy) {
    Write-Host ""
    Write-Host "Warning: Database took longer than expected to start" -ForegroundColor Yellow
    Write-Host "Check logs with: docker compose logs postgres" -ForegroundColor Yellow
}
