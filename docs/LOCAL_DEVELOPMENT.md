# Local Development Guide

This guide explains how to set up and run the Questions Hub application locally for development.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- IDE of your choice (Rider, Visual Studio, VS Code)
- No other service using port 5432

## Quick Start

The recommended workflow is to run PostgreSQL in Docker while running the Blazor app from your IDE:

```powershell
# 1. Start the database
.\start-dev-db.ps1

# 2. Run Blazor app from your IDE (F5 in Rider/VS)
#    App connects to localhost:5432 automatically

# 3. Stop the database when done
.\stop-dev-db.ps1
```

## Database Setup

### Starting the Database

```powershell
.\start-dev-db.ps1
```

This script:
- Sets up PostgreSQL with development credentials
- Creates the `questionshub` database and user
- Installs required extensions (unaccent, pg_trgm)
- Configures Ukrainian full-text search
- Waits until the database is healthy

**Expected Output:**
```
Starting PostgreSQL for local development...
Connection details:
  Host: localhost
  Port: 5432
  Database: questionshub
  Username: questionshub
  Password: dev_password_123

PostgreSQL is starting...
Waiting for database to be ready...
..........
Database is ready!

You can now run the Blazor app locally from your IDE
To stop: .\stop-dev-db.ps1
```

### Verifying the Database

Check that containers are running:
```powershell
docker ps
```

You should see:
- `questions-hub-db` (running, healthy)
- `questions-hub-db-setup` (exited with code 0)

Test database connection:
```powershell
docker exec -it questions-hub-db psql -U postgres -d questionshub -c "\du"
```

### Stopping the Database

```powershell
.\stop-dev-db.ps1
```

Data is preserved in `./postgres_data/` folder.

### Resetting the Database

To completely reset and start fresh:

```powershell
.\cleanup-db.ps1
```

This will prompt for confirmation before deleting all data.

## Running the Application

### From IDE (Recommended)

1. Ensure database is running (`.\start-dev-db.ps1`)
2. Open the project in your IDE
3. Press F5 or Run
4. App will be available at `https://localhost:5001` or `http://localhost:5000`

### From Command Line

```bash
cd QuestionsHub.Blazor
dotnet run
```

### Full Stack in Docker

To run everything in Docker (for testing the complete containerized setup):

```powershell
# Set environment variables
$env:POSTGRES_ROOT_PASSWORD = "dev_root_password"
$env:QUESTIONSHUB_PASSWORD = "dev_password_123"
$env:ADMIN_EMAIL = "admin@example.com"
$env:ADMIN_PASSWORD = "Admin123!"

# Build and start all services
docker compose --profile full up -d --build

# Access at http://localhost:8080
```

## Connection String

The development connection string is pre-configured in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=questionshub;Username=questionshub;Password=dev_password_123"
  }
}
```

## Database Scripts

All database setup scripts are located in `db/scripts/`:

| Script | Purpose |
|--------|---------|
| `01-extensions.sql` | Installs unaccent and pg_trgm extensions |
| `02-user-permissions.sql` | Creates questionshub user and grants permissions |
| `03-fts-setup.sql` | Configures Ukrainian full-text search |

These scripts are idempotent and run automatically when you start the database.

## EF Core Migrations

Create a new migration:
```bash
cd QuestionsHub.Blazor
dotnet ef migrations add <MigrationName> --output-dir Data/Migrations
```

Apply migrations:
```bash
dotnet ef database update
```

Migrations are also applied automatically when the application starts.

## Helper Scripts

| Script | Description |
|--------|-------------|
| `.\start-dev-db.ps1` | Start PostgreSQL for development |
| `.\stop-dev-db.ps1` | Stop PostgreSQL (preserves data) |
| `.\dev-db-logs.ps1` | View PostgreSQL logs |
| `.\cleanup-db.ps1` | Delete database and start fresh |
| `.\check-db-location.ps1` | Check database status and location |

## Troubleshooting

### Port 5432 Already in Use

```powershell
# Find what's using the port
netstat -ano | findstr :5432

# Stop any previous Docker containers
docker stop questions-hub-db
docker rm questions-hub-db
```

### Database Won't Start

```powershell
# Check logs
.\dev-db-logs.ps1

# Or check db-setup logs
docker logs questions-hub-db-setup

# Clean restart
.\cleanup-db.ps1
.\start-dev-db.ps1
```

### Connection Refused from Blazor App

1. Verify database is running: `docker ps`
2. Check connection string in `appsettings.Development.json`
3. Ensure it uses `localhost:5432`

### db-setup Container Fails

```powershell
# Check setup logs
docker logs questions-hub-db-setup

# Restart just the setup
docker compose --profile dev restart db-setup
```

### Manual Database Connection

```powershell
# Connect as app user
$env:PGPASSWORD='dev_password_123'
psql -h localhost -p 5432 -U questionshub -d questionshub

# Or connect via Docker
docker exec -it questions-hub-db psql -U postgres -d questionshub
```

## Success Criteria

Your local setup is working correctly when:

- ✅ Docker containers start without errors
- ✅ Database accepts connections on localhost:5432
- ✅ `questionshub` user exists with proper permissions
- ✅ Blazor app connects and queries the database
- ✅ Data persists after stopping and restarting containers

