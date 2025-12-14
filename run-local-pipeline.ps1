# Quick Start Script for Testing CI/CD Locally with Act
# Save this as: run-local-pipeline.ps1

Write-Host "=== Questions Hub - Local CI/CD Testing ===" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is running
Write-Host "Checking Docker..." -ForegroundColor Yellow
$dockerRunning = docker ps 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}
Write-Host "✓ Docker is running" -ForegroundColor Green
Write-Host ""

# Check if act is installed
Write-Host "Checking act-cli..." -ForegroundColor Yellow
$actVersion = act --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ act is not installed. Install with: choco install act-cli" -ForegroundColor Red
    exit 1
}
Write-Host "✓ act is installed" -ForegroundColor Green
Write-Host ""

# Check if .secrets file exists
if (-not (Test-Path ".secrets")) {
    Write-Host "❌ .secrets file not found!" -ForegroundColor Red
    Write-Host "Please create .secrets file with your credentials." -ForegroundColor Yellow
    Write-Host "See docs/LOCAL_TESTING.md for details." -ForegroundColor Yellow
    exit 1
}
Write-Host "✓ .secrets file found" -ForegroundColor Green
Write-Host ""

# Show menu
Write-Host "Select an option:" -ForegroundColor Cyan
Write-Host "1. List workflows"
Write-Host "2. Dry run (see what would execute)"
Write-Host "3. Run full pipeline (as if pushed to main)"
Write-Host "4. Run with verbose output"
Write-Host "5. Run specific job: build-test-deploy"
Write-Host "6. Exit"
Write-Host ""

$choice = Read-Host "Enter your choice (1-6)"

switch ($choice) {
    "1" {
        Write-Host "`nListing workflows..." -ForegroundColor Yellow
        act --list
    }
    "2" {
        Write-Host "`nPerforming dry run..." -ForegroundColor Yellow
        act push --dryrun
    }
    "3" {
        Write-Host "`nRunning full pipeline..." -ForegroundColor Yellow
        Write-Host "⚠️  This will connect to VPS and deploy!" -ForegroundColor Red
        $confirm = Read-Host "Are you sure? (yes/no)"
        if ($confirm -eq "yes") {
            act push
        } else {
            Write-Host "Cancelled." -ForegroundColor Yellow
        }
    }
    "4" {
        Write-Host "`nRunning with verbose output..." -ForegroundColor Yellow
        act push -v
    }
    "5" {
        Write-Host "`nRunning build-test-deploy job..." -ForegroundColor Yellow
        act -j build-test-deploy
    }
    "6" {
        Write-Host "Exiting..." -ForegroundColor Yellow
        exit 0
    }
    default {
        Write-Host "Invalid choice!" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green

