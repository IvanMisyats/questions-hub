# Local CI/CD Testing with Act

This guide explains how to test the GitHub Actions pipeline locally using `act`.

## Prerequisites

- ✅ Docker Desktop installed and running
- ✅ `act-cli` installed via Chocolatey

## Setup

### 1. Configure Secrets

Edit the `.secrets` file with your actual credentials:

```bash
VPS_HOST=your-vps-ip-or-hostname
VPS_USER=your-ssh-username
VPS_SSH_KEY=-----BEGIN OPENSSH PRIVATE KEY-----
... your SSH private key ...
-----END OPENSSH PRIVATE KEY-----
REPO_TOKEN=ghp_yourGitHubPersonalAccessToken
POSTGRES_ROOT_PASSWORD=your-db-password
QUESTIONSHUB_PASSWORD=your-app-db-password
```

**Important**: Never commit `.secrets` file! It's already in `.gitignore`.

### 2. Configuration File

The `.actrc` file is already configured with sensible defaults:
- Uses `catthehacker/ubuntu:act-latest` Docker image
- Reads secrets from `.secrets` file
- Optimized for faster runs

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

1. **VPS Deployment**: The SSH/SCP steps will try to connect to your actual VPS
2. **GHCR Push**: Will try to push to GitHub Container Registry
3. **Resource Usage**: Docker builds can be resource-intensive

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

## Additional Resources

- Act Documentation: https://github.com/nektos/act
- GitHub Actions Docs: https://docs.github.com/en/actions

