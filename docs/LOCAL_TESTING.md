# Local CI/CD Testing with Act

This guide explains how to test the GitHub Actions pipeline locally using `act`.

## Prerequisites

- ✅ Docker Desktop installed and running
- ✅ `act-cli` installed via Chocolatey

## Setup

### 1. Configure Secrets

Copy `.secrets.template` to `.secrets` and fill in your actual values:

```bash
# For local testing only
VPS_HOST=your-vps-ip-or-hostname
VPS_USER=github-actions
VPS_SSH_KEY=-----BEGIN OPENSSH PRIVATE KEY-----
... your SSH private key ...
-----END OPENSSH PRIVATE KEY-----
REPO_TOKEN=ghp_yourGitHubPersonalAccessToken
```

**Important Notes:**
- Never commit `.secrets` file! It's already in `.gitignore`.
- Database passwords (`POSTGRES_ROOT_PASSWORD`, `QUESTIONSHUB_PASSWORD`) are stored in `.env` file on the VPS, not in GitHub Secrets.
- The VPS user should be `github-actions` (created during VPS setup).

### 2. Configuration File

Copy `.actrc.example` to `.actrc` for optimized settings:

```bash
cp .actrc.example .actrc
```

The configuration includes:
- Uses `catthehacker/ubuntu:act-latest` Docker image (includes Docker and more tools)
- Reads secrets from `.secrets` file
- Optimized for faster runs with `--pull=false` and `--rm`

## Running the Pipeline

### List Available Workflows

```powershell
act --list
```

### Dry Run (see what would execute)

```powershell
act --dryrun
```

### Run Specific Job

```powershell
# Run the build-test-deploy job
act -j build-test-deploy
```

### Run on Push Event (triggers main workflow)

```powershell
act push
```

### Run with Verbose Output

```powershell
act -v
```

### Run Specific Steps (for debugging)

You can't run individual steps, but you can:

1. **Skip deployment steps**: Comment out the VPS deployment steps in the workflow
2. **Test locally without VPS**: Use `--dryrun` to see execution plan
3. **Use different platforms**: 
   ```powershell
   act -P ubuntu-latest=ubuntu:latest
   ```

## Common Commands

```powershell
# Full pipeline run (as it would run on GitHub)
act push

# List all workflows and jobs
act --list

# Run with custom secrets file
act push --secret-file .secrets.local

# Run without pulling Docker images (faster)
act push --pull=false

# Keep containers for debugging
act push --reuse

# Clean up after failure
act push --rm
```

## Limitations

When running locally with `act`:

1. **VPS Deployment**: The SSH/SCP steps will try to connect to your actual VPS and deploy
2. **GHCR Push**: Will try to push to GitHub Container Registry (requires authentication)
3. **Resource Usage**: Docker builds can be resource-intensive
4. **VPS .env Required**: The deployment script expects `.env` file to exist on VPS at `~/questions-hub/.env`

## Testing Without Full Deployment

To test the build without deploying:

1. **Option 1**: Use workflow_dispatch trigger with manual run
   ```powershell
   act workflow_dispatch
   ```

2. **Option 2**: Comment out deployment steps temporarily in the workflow

3. **Option 3**: Create a separate test workflow file for local testing

## Debugging Tips

### View logs in real-time
```powershell
act -v push
```

### Check what secrets are needed
```powershell
act --list
```

### Run with insecure secrets (shows secrets in logs - NOT RECOMMENDED for production)
```powershell
act push --insecure-secrets
```

### Use specific Docker network
```powershell
act push --network bridge
```

## Environment Variables

You can override environment variables:

```powershell
act push --env PROJECT_NAME=myuser/myproject
```

## Troubleshooting

### Docker not found
- Ensure Docker Desktop is running
- Restart your terminal after installing Docker

### Act can't find workflow
- Make sure you're in the project root directory
- Check that `.github/workflows/ci-cd.yml` exists

### SSH connection fails
- Verify VPS credentials in `.secrets`
- Test SSH connection manually: `ssh -p 55055 user@host`

### Out of disk space
- Clean Docker: `docker system prune -a`
- Use smaller image: `-P ubuntu-latest=ubuntu:latest`

## VPS Setup Requirements

Before running the pipeline (locally or on GitHub), ensure the VPS is configured:

### 1. Create Deployment User

```bash
# SSH to VPS as root or admin user
sudo adduser github-actions
sudo usermod -aG docker github-actions
```

### 2. Setup SSH Key

Add your deployment SSH public key to the user's authorized_keys:

```bash
sudo -u github-actions mkdir -p /home/github-actions/.ssh
sudo -u github-actions nano /home/github-actions/.ssh/authorized_keys
# Paste your public key, save and exit
sudo chmod 700 /home/github-actions/.ssh
sudo chmod 600 /home/github-actions/.ssh/authorized_keys
sudo chown -R github-actions:github-actions /home/github-actions/.ssh
```

### 3. Create .env File on VPS

```bash
# As root, create the .env file for github-actions user
sudo -u github-actions mkdir -p /home/github-actions/questions-hub
sudo -u github-actions bash -c 'cat > /home/github-actions/questions-hub/.env << EOF
POSTGRES_ROOT_PASSWORD=your_secure_root_password
QUESTIONSHUB_PASSWORD=your_secure_app_password
EOF'

# Set proper permissions
sudo chmod 600 /home/github-actions/questions-hub/.env
```

### 4. Create Docker Network

```bash
sudo -u github-actions docker network create questions-hub-network
```

### 5. Verify Setup

```bash
# Test SSH connection
ssh -p 55055 github-actions@your-vps-host

# Verify docker access
docker ps

# Verify .env file exists
cat ~/questions-hub/.env
```

## Additional Resources

- Act Documentation: https://github.com/nektos/act
- GitHub Actions Docs: https://docs.github.com/en/actions

