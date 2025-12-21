#!/usr/bin/env pwsh
# Stop PostgreSQL development database

Write-Host "Stopping PostgreSQL development database..." -ForegroundColor Yellow

docker-compose --profile dev down

Write-Host "PostgreSQL stopped." -ForegroundColor Green
Write-Host "Data is preserved in ./postgres_data/" -ForegroundColor Cyan

