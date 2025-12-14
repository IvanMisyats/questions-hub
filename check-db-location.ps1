# Database Location Check Script
# Shows where your database is (or will be) stored

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Location Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if in correct directory
if (-not (Test-Path "docker-compose.yml")) {
    Write-Host "❌ Error: Not in project directory!" -ForegroundColor Red
    Write-Host "Please run from: C:\Projects\questions-hub\" -ForegroundColor Yellow
    exit 1
}

# Read .env file to get POSTGRES_DATA_PATH
$dbPath = "./postgres_data"  # Default
if (Test-Path ".env") {
    Write-Host "📄 Reading .env file..." -ForegroundColor White
    $envContent = Get-Content .env | Where-Object { $_ -match "^POSTGRES_DATA_PATH=" }
    if ($envContent) {
        $dbPath = ($envContent -split "=")[1].Trim()
        Write-Host "✅ Found POSTGRES_DATA_PATH: $dbPath" -ForegroundColor Green
    }
} else {
    Write-Host "⚠️  No .env file - using default: $dbPath" -ForegroundColor Yellow
}

Write-Host ""

# Convert to absolute path
if ($dbPath -like ".*") {
    $absolutePath = Join-Path (Get-Location) ($dbPath -replace "^\./", "")
} else {
    $absolutePath = $dbPath
}

Write-Host "📁 Database Location:" -ForegroundColor Cyan
Write-Host "   $absolutePath" -ForegroundColor White
Write-Host ""

# Check if folder exists
if (Test-Path $dbPath) {
    Write-Host "✅ Database folder EXISTS" -ForegroundColor Green
    
    # Calculate size
    $items = Get-ChildItem -Recurse $dbPath -ErrorAction SilentlyContinue
    $size = ($items | Measure-Object -Property Length -Sum).Sum
    $sizeMB = [math]::Round($size / 1MB, 2)
    $fileCount = $items.Count
    
    Write-Host "📊 Size: $sizeMB MB" -ForegroundColor White
    Write-Host "📝 Files: $fileCount" -ForegroundColor White
    
    # Check if Docker containers are running
    $containers = docker ps --filter "name=questions-hub" --format "{{.Names}}" 2>$null
    if ($containers) {
        Write-Host ""
        Write-Host "🐳 Docker Status:" -ForegroundColor Cyan
        docker ps --filter "name=questions-hub" --format "table {{.Names}}\t{{.Status}}" 2>$null
    }
} else {
    Write-Host "❌ Database folder DOES NOT EXIST yet" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "ℹ️  The folder will be created automatically when you run:" -ForegroundColor White
    Write-Host "   docker-compose up -d" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Quick Commands:" -ForegroundColor Cyan
Write-Host "  Start:   docker-compose up -d" -ForegroundColor White
Write-Host "  Stop:    docker-compose down" -ForegroundColor White
Write-Host "  Clean:   .\cleanup-db.ps1" -ForegroundColor White
Write-Host "  Logs:    docker logs questions-hub-web" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

