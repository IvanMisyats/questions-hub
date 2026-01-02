# Docker Compose Profiles

This document explains the different Docker Compose profiles used in the project.

## Overview

The project uses Docker profiles to support different deployment scenarios:

| Profile | Use Case | Services Started |
|---------|----------|------------------|
| **dev** | Local development in IDE | PostgreSQL + db-setup |
| **full** | Local testing of full stack | PostgreSQL + db-setup + web-local (builds from Dockerfile) |
| **production** | VPS deployment | PostgreSQL + db-setup + web (pulls from GHCR) |

## Development Profile (`dev`)

**Purpose:** Run only PostgreSQL in Docker while developing the Blazor app locally in your IDE.

**Services:**
- `postgres` - PostgreSQL database (accessible at `localhost:5432`)
- `db-setup` - Creates users, permissions, and configures FTS

**Usage:**
```powershell
.\start-dev-db.ps1
```

Or manually:
```powershell
$env:POSTGRES_ROOT_PASSWORD = "dev_root_password"
$env:QUESTIONSHUB_PASSWORD = "dev_password_123"
$env:POSTGRES_HOST_AUTH_METHOD = "trust"

docker-compose --profile dev up -d
```

**Connection String:**
```
Host=localhost;Port=5432;Database=questionshub;Username=questionshub;Password=dev_password_123
```

## Full Profile (`full`)

**Purpose:** Test the complete containerized stack locally, building the web app from source.

**Services:**
- `postgres` - PostgreSQL database
- `db-setup` - Database initialization
- `web-local` - Blazor app built from Dockerfile

**Usage:**
```powershell
$env:POSTGRES_ROOT_PASSWORD = "dev_root_password"
$env:QUESTIONSHUB_PASSWORD = "dev_password_123"

docker-compose --profile full up -d --build
```

Access the app at `http://localhost:8080`

**Note:** Uses `ASPNETCORE_ENVIRONMENT=Development` and default admin credentials.

## Production Profile (`production`)

**Purpose:** Deploy to VPS using pre-built image from GitHub Container Registry.

**Services:**
- `postgres` - PostgreSQL database
- `db-setup` - Database initialization
- `web` - Pre-built image from GHCR

**Usage (on VPS):**
```bash
# Load environment variables
set -a
source ~/.env
set +a

export POSTGRES_DATA_PATH=~/questions-hub/data/postgres
export MEDIA_PATH=~/questions-hub/media

docker-compose --profile production up -d
```

**Required Environment Variables:**
- `POSTGRES_ROOT_PASSWORD` - PostgreSQL superuser password
- `QUESTIONSHUB_PASSWORD` - Application database user password
- `ADMIN_EMAIL` - Admin user email
- `ADMIN_PASSWORD` - Admin user password

## Service Details

### postgres

PostgreSQL 16 (Alpine) with:
- Ukrainian hunspell dictionary files mounted
- Health check enabled
- Memory limit: 768MB
- Optimized for low-memory VPS (2GB RAM)

### db-setup

Runs SQL scripts from `db/scripts/` in order:
1. `01-extensions.sql` - Install unaccent, pg_trgm
2. `02-user-permissions.sql` - Create user, grant permissions
3. `03-fts-setup.sql` - Configure Ukrainian FTS

Exits after completion. Scripts are idempotent.

### web / web-local

| Aspect | web (production) | web-local (full) |
|--------|-----------------|------------------|
| Image source | GHCR | Built from Dockerfile |
| Environment | Production | Development |
| Admin credentials | Required from env | Defaults provided |

## Database Configuration

### Development Credentials
- **Root User:** postgres / dev_root_password
- **App User:** questionshub / dev_password_123

### Production Credentials
Set via environment variables (stored in `~/.env` on VPS):
```bash
POSTGRES_ROOT_PASSWORD=your_secure_root_password
QUESTIONSHUB_PASSWORD=your_secure_app_password
```

### Data Persistence
- All profiles use `${POSTGRES_DATA_PATH:-./postgres_data}` for data
- Data persists across container restarts
- Folder is in `.gitignore`

## Quick Reference

### Development
```powershell
.\start-dev-db.ps1    # Start
.\stop-dev-db.ps1     # Stop
.\dev-db-logs.ps1     # View logs
.\cleanup-db.ps1      # Reset database
```

### Full Stack Testing
```powershell
docker compose --profile full up -d --build
docker compose --profile full down
```

### Production (VPS)
```bash
docker compose --profile production up -d
docker compose --profile production down
docker compose --profile production logs -f
```

## Troubleshooting

### Database won't start
```powershell
# Check logs
docker compose --profile dev logs postgres

# Ensure port 5432 is free
netstat -ano | findstr :5432

# Clean restart
.\cleanup-db.ps1
.\start-dev-db.ps1
```

### db-setup fails
```powershell
# Check setup logs
docker logs questions-hub-db-setup

# Restart just the setup
docker compose --profile dev restart db-setup
```

### Web app can't connect to database
- Ensure `postgres` service is healthy
- Check that db-setup completed successfully
- Verify environment variables are set

