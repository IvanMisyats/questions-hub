# Local Development Setup

This guide is for local development only. For VPS deployment, see [LOCAL_TESTING.md](LOCAL_TESTING.md).

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop
- PostgreSQL (via Docker Compose)

## Database Setup

1. **Start PostgreSQL database:**
   
   Using helper script (recommended):
   ```powershell
   .\start-dev-db.ps1
   ```
   
   This script sets up PostgreSQL with trust authentication for local development, allowing your IDE-run application to connect to the containerized database.
   
   > **Note:** The script sets `POSTGRES_HOST_AUTH_METHOD=trust` which allows connections without strict password verification. This is safe for local development only.

2. **First time setup - initialize database:**
   If starting with an empty `postgres_data` folder (first time or after cleanup), the database and user will be created automatically by docker-compose.

3. **Apply database migrations:**
   Migrations are applied automatically when the application starts. You can also run them manually:
   ```bash
   cd QuestionsHub.Blazor
   dotnet ef database update
   ```

## Running the Application

```bash
cd QuestionsHub.Blazor
dotnet run
```

The application will be available at `https://localhost:5001` or `http://localhost:5000`.

## Stopping the Database

Using helper script:
```powershell
.\stop-dev-db.ps1
```

Or directly:
```bash
docker compose --profile dev down
```

### Resetting the Database

To completely reset the database and start fresh:

```powershell
.\cleanup-db.ps1
```

Or manually:
```powershell
docker-compose --profile dev down -v
Remove-Item -Recurse -Force postgres_data\*
.\start-dev-db.ps1
```

> **Important:** After cleaning up, you must restart with `.\start-dev-db.ps1` (not just `docker-compose up`) to ensure proper trust authentication is set for local development.

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

