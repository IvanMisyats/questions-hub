# VPS Deployment Guide

This guide explains how to set up and deploy the Questions Hub application on a VPS.

## Architecture Overview

The production deployment uses Docker Compose on the VPS with:
- **PostgreSQL** container for the database
- **db-setup** container for database initialization
- **Web** container for the Blazor application

The CI/CD pipeline automatically:
1. Builds the Docker image
2. Pushes to GitHub Container Registry (GHCR)
3. Copies deployment files to VPS
4. Deploys using `docker compose --profile production`

## VPS Requirements

- Docker and Docker Compose v2 installed
- SSH access for the `github-actions` user
- Ports: 8080 (web), 5432 (optional, for DB management)

## Initial VPS Setup

### 1. Install Docker and Compose

```bash
# Remove old versions
sudo apt-get remove docker docker-engine docker.io containerd runc docker-compose

# Install prerequisites
sudo apt-get update
sudo apt-get install ca-certificates curl gnupg

# Add Docker's GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Add Docker repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker + Compose
sudo apt-get update
sudo apt-get install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Verify
docker compose version
```

### 2. Create Deployment User

```bash
# SSH to VPS as root or admin user
sudo adduser github-actions
sudo usermod -aG docker github-actions
```

### 3. Setup SSH Key Authentication

Add your deployment SSH public key:

```bash
sudo -u github-actions mkdir -p /home/github-actions/.ssh
sudo -u github-actions nano /home/github-actions/.ssh/authorized_keys
# Paste your public key, save and exit

# Set permissions
sudo chmod 700 /home/github-actions/.ssh
sudo chmod 600 /home/github-actions/.ssh/authorized_keys
sudo chown -R github-actions:github-actions /home/github-actions/.ssh
```

### 4. Create Environment File

The `.env` file stores sensitive credentials and must be created on the VPS:

```bash
# Create directory
sudo -u github-actions mkdir -p /home/github-actions/questions-hub

# Create .env file
sudo -u github-actions bash -c 'cat > /home/github-actions/.env << EOF
POSTGRES_ROOT_PASSWORD=your_secure_root_password
QUESTIONSHUB_PASSWORD=your_secure_app_password
ADMIN_EMAIL=admin@yourdomain.com
ADMIN_PASSWORD=YourSecureAdminPassword123!
EOF'

# Set secure permissions
sudo chmod 600 /home/github-actions/.env
```

**Important:**
- Change all passwords to secure values
- Admin password must meet requirements: 8+ chars, digit, uppercase, lowercase, special char
- The `.env` file is NOT committed to Git - it lives only on the VPS

### 5. Create Required Directories

```bash
sudo -u github-actions mkdir -p /home/github-actions/questions-hub/data/postgres
sudo -u github-actions mkdir -p /home/github-actions/questions-hub/uploads/handouts
sudo -u github-actions mkdir -p /home/github-actions/questions-hub/uploads/packages
sudo -u github-actions mkdir -p /home/github-actions/questions-hub/keys

# Keys directory needs restricted permissions (only owner can read/write)
sudo chmod 700 /home/github-actions/questions-hub/keys

# Uploads directory needs correct ownership for Docker container
# The container runs as UID 1000, so we need to set ownership accordingly
sudo chown -R 1000:1000 /home/github-actions/questions-hub/uploads
sudo chmod -R 755 /home/github-actions/questions-hub/uploads
```

### 6. Verify Setup

```bash
# Test SSH connection
ssh -p 55055 github-actions@your-vps-host

# Verify Docker access
docker ps

# Verify Compose version
docker compose version

# Verify .env file exists
cat ~/.env
```

## GitHub Secrets Configuration

Configure these secrets in your GitHub repository (Settings → Secrets → Actions):

| Secret | Description |
|--------|-------------|
| `VPS_HOST` | VPS IP address or hostname |
| `VPS_USER` | Deployment user (`github-actions`) |
| `VPS_SSH_KEY` | Private SSH key for authentication |
| `REPO_TOKEN` | GitHub Personal Access Token with `packages:write` |

**Note:** Database passwords are stored in `.env` on VPS, not in GitHub Secrets.

## Deployment Process

### Automatic Deployment

Every push to `main` branch triggers the CI/CD pipeline:

1. Build and test the .NET application
2. Build Docker image
3. Push image to GHCR
4. Copy `docker-compose.yml` and `db/` folder to VPS
5. Deploy using `docker compose --profile production up -d`

### Manual Deployment

SSH to VPS and run:

```bash
cd ~/questions-hub

# Load environment variables
set -a
source ~/.env
set +a

# Export for docker compose
export POSTGRES_ROOT_PASSWORD
export QUESTIONSHUB_PASSWORD
export ADMIN_EMAIL
export ADMIN_PASSWORD
export POSTGRES_DATA_PATH=~/questions-hub/data/postgres
export UPLOADS_PATH=~/questions-hub/uploads
export KEYS_PATH=~/questions-hub/keys

# Pull latest image
docker pull ghcr.io/ivanmisyats/questions-hub:latest

# Deploy
docker compose --profile production up -d
```

## File Locations on VPS

| Path | Purpose |
|------|---------|
| `~/questions-hub/` | Application files |
| `~/questions-hub/docker-compose.yml` | Docker Compose configuration |
| `~/questions-hub/db/` | Database scripts and dictionaries |
| `~/questions-hub/data/postgres/` | PostgreSQL data files |
| `~/questions-hub/uploads/` | Uploaded files (handouts, packages) |
| `~/questions-hub/keys/` | Data Protection encryption keys |
| `~/.env` | Environment variables |

## Monitoring and Logs

```bash
# View all containers
docker ps

# View logs
docker compose --profile production logs -f

# View specific service logs
docker logs questions-hub-web
docker logs questions-hub-db

# Check container health
docker inspect --format='{{.State.Health.Status}}' questions-hub-db
```

## Troubleshooting

### Containers Won't Start

```bash
# Check logs
docker compose --profile production logs

# Verify environment variables are loaded
echo $POSTGRES_ROOT_PASSWORD

# Check disk space
df -h
```

### Database Connection Issues

```bash
# Check database is healthy
docker exec questions-hub-db pg_isready -U postgres -d questionshub

# View database logs
docker logs questions-hub-db

# Connect to database
docker exec -it questions-hub-db psql -U postgres -d questionshub
```

### Web App Won't Start

```bash
# Check web app logs
docker logs questions-hub-web

# Verify database connection
docker exec questions-hub-web env | grep ConnectionStrings
```

### Rollback Deployment

```bash
# Stop current deployment
docker compose --profile production down

# Pull specific version (if tagged)
docker pull ghcr.io/ivanmisyats/questions-hub:v1.0.0

# Restart with specific version
docker compose --profile production up -d
```

## Security Considerations

- SSH key authentication only (no password login)
- Database passwords in `.env` file with 600 permissions
- `github-actions` user has no sudo access
- Containers run with memory limits
- Media directory has write access for file uploads (ownership must match container user)
- Keys directory has restrictive permissions (700)

