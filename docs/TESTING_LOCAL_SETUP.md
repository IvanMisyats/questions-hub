# Testing the Local Development Setup

This guide helps you verify that the local development setup is working correctly.

## Prerequisites

- Docker Desktop must be running
- No other service using port 5432

## Test Steps

### 1. Check Docker is Running

```powershell
docker --version
docker ps
```

If Docker is not running, start Docker Desktop.

### 2. Start the Development Database

```powershell
.\start-dev-db.ps1
```

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

You can now run the Blazor app locally from Rider (F5)
To stop: .\stop-dev-db.ps1
```

### 3. Verify Database is Running

```powershell
docker ps
```

You should see two containers:
- `questions-hub-db` (running)
- `questions-hub-db-setup` (exited with code 0)

### 4. Test Database Connection

```powershell
# Using psql (if installed)
psql -h localhost -p 5432 -U questionshub -d questionshub

# Or using Docker
docker exec -it questions-hub-db psql -U postgres -d questionshub -c "\du"
```

You should see the `questionshub` user listed.

### 5. Run the Blazor App from Your IDE

1. Open the project in your IDE (Rider, Visual Studio, VS Code, etc.)
2. Press F5 (Debug) or use the Run command
3. Wait for the app to start
4. Open browser to `https://localhost:5001` or `http://localhost:5000`

**Expected:** App loads successfully and can query the database.

### 6. Check Logs (If Issues)

```powershell
# PostgreSQL logs
.\dev-db-logs.ps1

# Or directly
docker-compose logs postgres
docker-compose logs db-setup
```

### 7. Stop the Database

```powershell
.\stop-dev-db.ps1
```

**Expected Output:**
```
Stopping PostgreSQL development database...
[+] Running 2/2
 ✔ Container questions-hub-db-setup  Removed
 ✔ Container questions-hub-db        Removed
PostgreSQL stopped.
Data is preserved in ./postgres_data/
```

## Troubleshooting

### Port 5432 Already in Use

Find and stop the process using port 5432:

```powershell
# Find process
netstat -ano | findstr :5432

# Stop Docker container if it's a previous instance
docker stop questions-hub-db
docker rm questions-hub-db
```

### Database Won't Start

```powershell
# Check Docker Compose config
docker-compose config

# View detailed logs
docker-compose --profile dev logs

# Clean restart
docker-compose --profile dev down -v
Remove-Item -Recurse -Force postgres_data
.\start-dev-db.ps1
```

### Connection Refused from Blazor App

1. Verify database is running: `docker ps`
2. Check connection string in `appsettings.Development.json`
3. Ensure it matches:
   ```json
   "DefaultConnection": "Host=localhost;Port=5432;Database=questionshub;Username=questionshub;Password=dev_password_123"
   ```

### db-setup Container Fails

```powershell
# Check setup logs
docker logs questions-hub-db-setup

# Common issues:
# - Wrong password: Check POSTGRES_ROOT_PASSWORD environment variable
# - Database not ready: The healthcheck should handle this, but you can manually restart
docker-compose --profile dev restart db-setup
```

## Manual Testing with psql

If you have PostgreSQL client tools installed:

```powershell
# Connect as app user
$env:PGPASSWORD='dev_password_123'
psql -h localhost -p 5432 -U questionshub -d questionshub

# Test queries
\dt                  # List tables
\du                  # List users
SELECT version();    # PostgreSQL version
```

## Success Criteria

✅ Docker containers start without errors  
✅ Database accepts connections on localhost:5432  
✅ `questionshub` user exists with proper permissions  
✅ Blazor app can connect and query the database  
✅ Data persists after stopping and restarting containers  
✅ Logs are accessible via scripts or docker-compose commands  

## Next Steps

Once everything is working:

1. Apply Entity Framework migrations:
   ```bash
   cd QuestionsHub.Blazor
   dotnet ef database update
   ```

2. Run the application and test features

3. For production deployment, see `docs/LOCAL_TESTING.md`

