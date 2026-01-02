# Clear Docker Logs Script
# Clears logs for Questions Hub containers

param(
    [switch]$All,
    [string]$ContainerName = "",
    [switch]$Help
)

function Show-Help {
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Clear Docker Logs Script" -ForegroundColor Cyan
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host "  .\clear-docker-logs.ps1 -All" -ForegroundColor White
    Write-Host "    Clear logs for all questions-hub containers" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  .\clear-docker-logs.ps1 -ContainerName <name>" -ForegroundColor White
    Write-Host "    Clear logs for specific container" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  .\clear-docker-logs.ps1 -All"
    Write-Host "  .\clear-docker-logs.ps1 -ContainerName questions-hub-web"
    Write-Host ""
    Write-Host "Recommended:" -ForegroundColor Yellow
    Write-Host "  docker compose down && docker compose up -d" -ForegroundColor Cyan
    Write-Host "  (Recreates containers, clearing logs automatically)" -ForegroundColor Gray
}

function Clear-ContainerLogs {
    param([string]$Name)

    Write-Host "📝 Checking container: $Name" -ForegroundColor Cyan

    # Check if container exists
    $exists = docker ps -a --filter "name=^${Name}$" --format "{{.Names}}" 2>$null

    if (-not $exists) {
        Write-Host "❌ Container '$Name' not found" -ForegroundColor Red
        return
    }

    # Get container status
    $status = docker inspect --format='{{.State.Status}}' $Name 2>$null

    if ($status -eq "running") {
        Write-Host "⚠️  Container is running. Consider stopping it first for clean log truncation." -ForegroundColor Yellow
        $confirm = Read-Host "Stop container to clear logs? (y/n)"

        if ($confirm -eq "y") {
            Write-Host "⏹️  Stopping container..." -ForegroundColor Yellow
            docker stop $Name | Out-Null
            $wasStopped = $true
        } else {
            Write-Host "⏭️  Skipping log clear for running container" -ForegroundColor Yellow
            return
        }
    }

    # Get log path
    $logPath = docker inspect --format='{{.LogPath}}' $Name 2>$null

    if ($logPath) {
        try {
            # Get log size before clearing
            if (Test-Path $logPath) {
                $sizeBefore = (Get-Item $logPath).Length / 1MB
                Write-Host "📊 Current log size: $([math]::Round($sizeBefore, 2)) MB" -ForegroundColor White

                # Clear the log file
                Clear-Content $logPath -ErrorAction Stop
                Write-Host "✅ Logs cleared for $Name" -ForegroundColor Green
            } else {
                Write-Host "⚠️  Log file not found at: $logPath" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "❌ Could not clear logs: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ Could not get log path for $Name" -ForegroundColor Red
    }

    # Restart container if we stopped it
    if ($wasStopped) {
        Write-Host "▶️  Restarting container..." -ForegroundColor Yellow
        docker start $Name | Out-Null
        Write-Host "✅ Container restarted" -ForegroundColor Green
    }

    Write-Host ""
}

# Main script logic
if ($Help) {
    Show-Help
    exit 0
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Docker Log Cleaner" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

if ($All) {
    Write-Host "🔍 Finding all questions-hub containers..." -ForegroundColor Cyan
    $containers = docker ps -a --filter "name=questions-hub" --format "{{.Names}}" 2>$null

    if ($containers) {
        Write-Host "📦 Found containers: $($containers -join ', ')" -ForegroundColor White
        Write-Host ""

        foreach ($container in $containers) {
            Clear-ContainerLogs -Name $container
        }
    } else {
        Write-Host "❌ No questions-hub containers found" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "💡 Tip: Start containers with 'docker compose up -d'" -ForegroundColor Cyan
    }
} elseif ($ContainerName) {
    Clear-ContainerLogs -Name $ContainerName
} else {
    Write-Host "❌ No parameters specified!" -ForegroundColor Red
    Write-Host ""
    Show-Help
    exit 1
}

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "💡 Recommendation:" -ForegroundColor Yellow
Write-Host "For a complete fresh start:" -ForegroundColor White
Write-Host "  docker compose down" -ForegroundColor Cyan
Write-Host "  docker compose up -d" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

