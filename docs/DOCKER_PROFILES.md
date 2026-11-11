# Docker Compose Profiles

The profiles in the repo-root `docker-compose.yml` are for **local development only**.

> **Production is no longer a profile.** It lives in its own file, `infra/docker-compose.yml`,
> installed on the VPS at `/srv/questions-hub/deploy/docker-compose.yml`. Production runs against
> an unprivileged **rootless** Docker daemon with dropped capabilities, a read-only root
> filesystem, a named Postgres volume and no published database port — settings that have no
> business in a dev file, and that were too easy to ship by accident while both lived together.
> See [`VPS_DEPLOYMENT.md`](VPS_DEPLOYMENT.md).

## Overview

| Profile | Use Case | Services Started |
|---------|----------|------------------|
| **dev** | Local development in IDE | PostgreSQL + db-setup |
| **full** | Local testing of full stack | PostgreSQL + db-setup + web-local (builds from Dockerfile) |

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

## Production (no longer a profile)

Production lives in `infra/docker-compose.yml`, installed on the VPS at
`/srv/questions-hub/deploy/docker-compose.yml` and run by `/usr/local/bin/qh-deploy` as the
unprivileged `qh` user. Nothing about it is driven from this repo's root compose file.

**Services:** `postgres` (named volume, no published port) → `db-setup` → `web` (GHCR image).

**Environment** comes from `/srv/questions-hub/deploy/app.env` (`root:qh` 0640) — template at
`infra/app.env.example`. Full details: [`VPS_DEPLOYMENT.md`](VPS_DEPLOYMENT.md).

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
Set in `/srv/questions-hub/deploy/app.env` on the VPS (`root:qh` 0640 — the app user can read it,
only root can change it):
```bash
POSTGRES_ROOT_PASSWORD=your_secure_root_password
QUESTIONSHUB_PASSWORD=your_secure_app_password
```

### Data Persistence
- **Dev profiles** use `${POSTGRES_DATA_PATH:-./postgres_data}` (bind mount, gitignored).
- **Production** uses a named Docker volume (`questions-hub_pgdata`) inside the `qh` user's
  rootless Docker root. Backups are logical `pg_dump`s, so the volume is never copied directly.

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
Production uses a separate file and runs as the unprivileged `qh` user:
```bash
cd /srv/questions-hub/deploy
docker compose --env-file app.env ps
docker compose --env-file app.env logs -f web
/usr/local/bin/qh-deploy          # pull + converge (what CI triggers)
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

