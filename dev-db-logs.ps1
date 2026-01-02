#!/usr/bin/env pwsh
# View PostgreSQL logs

Write-Host "Showing PostgreSQL logs (Ctrl+C to exit)..." -ForegroundColor Cyan
docker compose --profile dev logs -f postgres

