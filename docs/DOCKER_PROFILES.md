# Docker Compose Profiles

This document explains the different Docker Compose profiles used in the project.

## Overview

The project uses Docker profiles to support different deployment scenarios:

| Profile | Use Case | Services Started | Command |
|---------|----------|------------------|---------|
| **dev** | Local development in Rider | PostgreSQL only | `docker-compose --profile dev up -d` |
| **production** | VPS deployment | PostgreSQL + Web | `docker-compose --profile production up -d` |
| **full** | Local testing of full stack | PostgreSQL + Web | `docker-compose --profile full up -d` |

## Development Profile (`dev`)

**Purpose:** Run only PostgreSQL in Docker while developing the Blazor app locally in your IDE.

**Services:**
- PostgreSQL (accessible at `localhost:5432`)
- db-setup (creates users and permissions)

**Workflow:**
1. Start database: `.\start-dev-db.ps1`
2. Run Blazor app from your IDE (F5 in Rider/VS)
3. Stop database: `.\stop-dev-db.ps1`

**Connection String:**
```
Host=localhost;Port=5432;Database=questionshub;Username=questionshub;Password=dev_password_123
```

## Production Profile (`production`)

**Purpose:** Deploy both PostgreSQL and the web application on a VPS.

**Services:**
- PostgreSQL (internal Docker network + exposed on 5432 for management)
- Web application (exposed on port 8080)
- db-setup (creates users and permissions)

**Workflow:**
```bash
# On VPS
docker-compose --profile production up -d
```

**Environment Variables:**
- `QUESTIONSHUB_PASSWORD`: Password for questionshub database user (default: dev_password_123)
- `POSTGRES_ROOT_PASSWORD`: Password for postgres superuser (default: root_dev_password)
- `POSTGRES_DATA_PATH`: Path to database files (default: ./postgres_data)

## Full Profile (`full`)

**Purpose:** Test the complete containerized stack locally.

**Services:**
- Everything (PostgreSQL + Web)

**Workflow:**
```bash
docker-compose --profile full up -d
```

Access the app at `http://localhost:8080`

## Common Configuration

### Database Credentials (Development)
- **Root User:** postgres / root_dev_password
- **App User:** questionshub / dev_password_123

### Database Credentials (Production)
Set via environment variables or `.env` file:
```bash
POSTGRES_ROOT_PASSWORD=your_secure_root_password
QUESTIONSHUB_PASSWORD=your_secure_app_password
```

### Data Persistence
- All profiles use the same `postgres_data/` folder
- Data persists across container restarts
- Folder is in `.gitignore`

## Quick Reference

### Development
```powershell
# Start
.\start-dev-db.ps1

# Stop
.\stop-dev-db.ps1

# Logs
.\dev-db-logs.ps1
```

### Production (VPS)
```bash
# Start
docker-compose --profile production up -d

# Stop
docker-compose --profile production down

# Logs
docker-compose logs -f
```

### Full Stack Testing
```bash
# Start
docker-compose --profile full up -d

# Stop
docker-compose --profile full down
```

## Troubleshooting

### Database won't start
```bash
# Check logs
docker-compose logs postgres

# Ensure no other service is using port 5432
netstat -ano | findstr :5432

# Clean restart
docker-compose --profile dev down -v
# Delete postgres_data/ folder
docker-compose --profile dev up -d
```

### Permission denied errors
The db-setup container creates the necessary users and permissions. If you see permission errors:
```bash
# Restart db-setup
docker-compose --profile dev restart db-setup

# Check setup logs
docker-compose logs db-setup
```

