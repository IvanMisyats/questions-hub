# Documentation

This folder contains documentation for the Questions Hub project.

## Files

### [SITE_SPECIFICATION.md](SITE_SPECIFICATION.md)
Complete site specification and feature list:
- Purpose and overview of the project
- Domain model (Package, Tour, Question, User)
- All implemented features with details
- Future features (planned but not implemented)
- Quick reference for what works and what doesn't

### [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)
Guide for setting up local development environment:
- Running the app locally with Docker Compose
- Database setup and migrations
- Local .NET development without Docker

### [LOCAL_TESTING.md](LOCAL_TESTING.md)
Guide for testing the CI/CD pipeline locally using `act`:
- Running GitHub Actions workflows locally
- VPS deployment testing
- Secrets configuration
- Complete VPS setup instructions

## Quick Links

### For Developers
- Start here: [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)
- Set up your local environment with Docker Compose
- Uses `docker-compose.yml` for both web app and PostgreSQL

### For DevOps / CI/CD Testing
- Start here: [LOCAL_TESTING.md](LOCAL_TESTING.md)
- Test the deployment pipeline locally with `act`
- Requires `.secrets` file configuration
- Can deploy to actual VPS

### For VPS Setup
- See section "VPS Setup Requirements" in [LOCAL_TESTING.md](LOCAL_TESTING.md)
- Create `github-actions` user
- Configure SSH keys
- Create `.env` file with database passwords
- Setup Docker network

## Project Structure

```
questions-hub/
├── QuestionsHub.Blazor/         # Main application
├── .github/workflows/           # CI/CD pipeline
├── docs/                        # Documentation (you are here)
│   ├── LOCAL_DEVELOPMENT.md
│   ├── LOCAL_TESTING.md
│   └── README.md
├── docker-compose.yml           # For LOCAL development only
├── .env.example                 # Local environment template
├── .secrets.template            # Act testing secrets template
└── README.md                    # Project overview
```

## Key Differences: Local vs VPS

| Aspect | Local Development | VPS Production |
|--------|------------------|----------------|
| Orchestration | `docker-compose.yml` | Direct `docker run` commands |
| Database passwords | `.env` in project root | `~/questions-hub/.env` on VPS |
| Build method | Build from source | Pull pre-built image from GHCR |
| User | Your local user | `github-actions` user |
| Directory | Project folder | `/home/github-actions/questions-hub` |
| Network | Docker default | Custom `questions-hub-network` |

## Getting Started

1. **Local Development**:
   ```bash
   # Clone repo
   git clone https://github.com/IvanMisyats/questions-hub.git
   cd questions-hub
   
   # Follow LOCAL_DEVELOPMENT.md
   docker compose up -d
   ```

2. **Test CI/CD Locally**:
   ```bash
   # Install act-cli
   choco install act-cli
   
   # Configure secrets
   cp .secrets.template .secrets
   # Edit .secrets with your credentials
   
   # Run pipeline
   act push
   ```

3. **Setup VPS**:
   - Follow "VPS Setup Requirements" in LOCAL_TESTING.md
   - Configure GitHub Secrets in repository settings
   - Push to main branch to trigger deployment

