# Local Development Setup

This guide is for local development only. For VPS deployment, see [LOCAL_TESTING.md](LOCAL_TESTING.md).

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop
- PostgreSQL (via Docker Compose)

## Database Setup

1. **Start PostgreSQL database:**
   ```bash
   docker compose up -d
   ```
   
   Note: `docker-compose.yml` is used for local development. VPS deployment uses direct `docker run` commands.

2. **Configure connection string (first time only):**
   ```bash
   cd QuestionsHub.Blazor
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=questionshub;Username=questionshub;Password=dev_password_123"
   ```

3. **Apply database migrations:**
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

```bash
docker compose down
```

To also remove the data volume:
```bash
docker compose down -v
```

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

