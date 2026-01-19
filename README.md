# Questions Hub

This project is an online database of Ukrainian questions for the game "What? Where? When?" (Що? Де? Коли?).

## Features

**Implemented:**
- Structured storage of tournaments, packages, and questions with navigation
- Package management (create, edit, delete) for editors
- User authentication and role-based access control
- Media support (images, audio, video) in questions
- Ukrainian full-text search configuration
- Efficient search by text over questions

**Planned:**
- Comments and ratings on questions

## Technology Stack

- **Backend:** C#, ASP.NET Core 10, Blazor Server
- **Frontend:** HTML, CSS, Bootstrap
- **Database:** PostgreSQL 16 with Ukrainian FTS
- **Containerization:** Docker, Docker Compose
- **CI/CD:** GitHub Actions, GitHub Container Registry

## Documentation

| Guide | Description |
|-------|-------------|
| [Local Development](docs/LOCAL_DEVELOPMENT.md) | Set up local dev environment |
| [VPS Deployment](docs/VPS_DEPLOYMENT.md) | Deploy to production VPS |
| [Docker Profiles](docs/DOCKER_PROFILES.md) | Understanding dev, full, and production profiles |
| [Site Specification](docs/SITE_SPECIFICATION.md) | Complete feature specification |

## Quick Start (Local Development)

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- IDE (Rider, Visual Studio, or VS Code)

### Start Developing

```powershell
# 1. Clone the repository
git clone https://github.com/IvanMisyats/questions-hub.git
cd questions-hub

# 2. Start the database
.\start-dev-db.ps1

# 3. Run Blazor app from your IDE (F5)
#    App is available at https://localhost:5001

# 4. Stop the database when done
.\stop-dev-db.ps1
```

### Full Stack in Docker

To test the complete containerized setup:

```powershell
# Set environment variables
$env:POSTGRES_ROOT_PASSWORD = "dev_root_password"
$env:QUESTIONSHUB_PASSWORD = "dev_password_123"

# Build and start all services
docker compose --profile full up -d --build

# Access at http://localhost:8080
```

## Helper Scripts

| Script | Description |
|--------|-------------|
| `.\start-dev.ps1` | Start dev services (DB) |
| `.\stop-dev.ps1` | Stop dev services |
| `.\start-dev-db.ps1` | Start PostgreSQL only |
| `.\stop-dev-db.ps1` | Stop PostgreSQL (preserves data) |
| `.\dev-db-logs.ps1` | View PostgreSQL logs |
| `.\cleanup-db.ps1` | Delete database and start fresh |
| `.\check-db-location.ps1` | Check database status |

## Project Structure

```
questions-hub/
├── QuestionsHub.Blazor/         # Main Blazor application
│   ├── Components/              # Blazor components and pages
│   ├── Controllers/             # API controllers
│   ├── Data/                    # EF Core context and migrations
│   ├── Domain/                  # Domain models
│   └── Infrastructure/          # Utilities and helpers
├── db/                          # Database configuration
│   ├── dictionaries/            # Ukrainian FTS dictionary files
│   └── scripts/                 # SQL initialization scripts
├── docs/                        # Documentation
├── media/                       # Sample media files
├── .github/workflows/           # CI/CD pipeline
└── docker-compose.yml           # Docker Compose configuration
```

## Deployment

The application is deployed automatically via GitHub Actions:

1. Push to `main` branch triggers the CI/CD pipeline
2. Docker image is built and pushed to GitHub Container Registry
3. Deployment files are copied to VPS
4. Application is deployed using `docker compose --profile production`

See [VPS Deployment Guide](docs/VPS_DEPLOYMENT.md) for setup instructions.

## Database

PostgreSQL 16 with:
- Ukrainian hunspell dictionary for full-text search
- Extensions: `unaccent`, `pg_trgm`
- Optimized for low-memory VPS (2GB RAM)

Database scripts are in `db/scripts/` and run automatically on container start.

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

## License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.
See the [LICENSE](LICENSE) file for details.

By contributing, you agree to the terms in [CLA.md](CLA.md).


