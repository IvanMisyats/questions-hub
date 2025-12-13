# Deployment Information

## VPS Resource Analysis

Your VPS specs:
- **vCore**: 1
- **RAM**: 2 GB
- **Storage**: 20 GB
- **Docker Version**: 29.1.3, build f52814d

### Is this sufficient?

**YES**, your VPS is sufficient for hosting both .NET app and PostgreSQL with low traffic:

#### Expected Resource Usage:
- **PostgreSQL (Alpine)**: ~100-200 MB RAM (with optimized config)
- **.NET App (Alpine)**: ~150-250 MB RAM
- **Docker overhead**: ~50 MB
- **System overhead**: ~300-500 MB

**Total estimated usage**: ~600-1000 MB out of 2 GB RAM ✅

#### Optimizations Applied:
1. **Alpine Linux images** (~100MB vs ~200MB for standard images)
2. **PostgreSQL tuned for low memory**:
   - `shared_buffers=128MB`
   - `work_mem=4MB`
   - `max_connections=20`
3. **Memory limits** in docker-compose:
   - Web app: 512 MB limit
   - PostgreSQL: 768 MB limit

### Alternative: Raspberry Pi

**Raspberry Pi 4 (4GB or 8GB)** would also work well:
- Similar or better specs than your VPS
- Pros: One-time cost, full control
- Cons: Need static IP or dynamic DNS, power/network reliability, no professional support

**Recommendation**: Start with your OVH VPS. It's more reliable for production.

## PostgreSQL Connection String

### Format
```
Host=postgres;Port=5432;Database=questionshub;Username=questionshub;Password=YOUR_PASSWORD
```

### Explanation
- **Host=postgres**: Uses Docker Compose service name for internal networking
- **Port=5432**: Default PostgreSQL port (internal to Docker network)
- **Database=questionshub**: Database name
- **Username=questionshub**: Database user
- **Password**: Set via environment variable

### GitHub Secrets Configuration

You need to set these secrets in your GitHub repository:

1. **POSTGRES_PASSWORD**: 
   - Strong password for PostgreSQL
   - Example: `MyStr0ng_P@ssw0rd_2024!`
   - Used in both the connection string and PostgreSQL container

2. **VPS_HOST**: Your VPS IP or domain
3. **VPS_USER**: SSH username (usually `root` or `ubuntu`)
4. **VPS_SSH_KEY**: Private SSH key for authentication
5. **REPO_TOKEN**: GitHub token with package read permissions

## Database Persistence

### On VPS (Production)
PostgreSQL data is persisted using Docker volumes:
```yaml
volumes:
  - postgres_data:/var/lib/postgresql/data
```

This creates a named volume `postgres_data` that survives container restarts and updates.

**Location on VPS**: `/var/lib/docker/volumes/questions-hub_postgres_data/_data`

### On Local Machine (Development)
Same setup - data persists in Docker volumes.

**To backup data from VPS**:
```bash
# On your VPS
docker exec questions-hub-db pg_dump -U questionshub questionshub > backup.sql

# Download to local
scp -P 55055 user@your-vps:/path/backup.sql ./backup.sql

# Restore locally
docker exec -i questions-hub-db psql -U questionshub questionshub < backup.sql
```

**To backup data to your local machine regularly**:
Consider setting up automated backups using a cron job on the VPS that uploads to cloud storage or your local machine.

## How the CI/CD Pipeline Works

1. **Build**: Creates .NET Docker image with Alpine Linux
2. **Push**: Uploads image to GitHub Container Registry (GHCR)
3. **Copy**: Transfers docker-compose.yml to VPS
4. **Deploy**: 
   - Pulls latest .NET image from GHCR
   - Pulls PostgreSQL:16-alpine from Docker Hub
   - Creates .env file with secrets
   - Modifies docker-compose.yml to use pre-built image
   - Starts both services with `docker-compose up -d`

## Accessing Your Services

- **Web App**: `http://your-vps-ip:8080`
- **PostgreSQL**: `your-vps-ip:5432` (exposed, but should be firewalled)

**Security Recommendation**: Use firewall rules to only allow:
- Port 8080 (HTTP) or 443 (HTTPS with reverse proxy)
- Port 55055 (SSH)
- Block direct PostgreSQL access (5432) from external IPs

## Monitoring Resource Usage

SSH into your VPS and run:
```bash
# Check Docker container stats
docker stats

# Check overall system resources
htop

# Check disk usage
df -h

# Check Docker volumes
docker volume ls
docker volume inspect questions-hub_postgres_data
```

## Next Steps

1. ✅ Set `POSTGRES_PASSWORD` secret in GitHub
2. ✅ Ensure other secrets (VPS_HOST, VPS_USER, VPS_SSH_KEY, REPO_TOKEN) are set
3. ✅ Push changes to trigger deployment
4. ⚠️ Consider adding HTTPS with reverse proxy (nginx/caddy)
5. ⚠️ Set up automated database backups
6. ⚠️ Configure firewall rules on VPS

