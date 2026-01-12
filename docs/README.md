# Documentation

This folder contains documentation for the Questions Hub project.

## Guides

### [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)
Complete guide for setting up local development environment:
- Quick start with helper scripts
- Running PostgreSQL in Docker
- Running Blazor app from IDE
- Database management and migrations
- Troubleshooting common issues

### [VPS_DEPLOYMENT.md](VPS_DEPLOYMENT.md)
Guide for deploying to production VPS:
- Initial VPS setup
- Creating deployment user and SSH keys
- Environment configuration
- Manual and automatic deployment
- Monitoring and troubleshooting

### [DOCKER_PROFILES.md](DOCKER_PROFILES.md)
Understanding Docker Compose profiles:
- `dev` - Database only for local development
- `full` - Full stack built from source
- `production` - Full stack with pre-built image

### [SITE_SPECIFICATION.md](SITE_SPECIFICATION.md)
Complete site specification and feature list:
- Project overview and purpose
- Domain model (Package, Tour, Question, Author, User)
- All implemented features with details
- API endpoints
- Future features (planned)

### [AUTHENTICATION.md](AUTHENTICATION.md)
Authentication and authorization details:
- User roles and permissions
- Login/registration flow
- Security considerations

### [SEARCH.md](SEARCH.md)
Search functionality documentation:
- Full-text search implementation
- Ukrainian language support
- Search configuration

### [MEDIA_SETUP.md](MEDIA_SETUP.md)
Media handling documentation:
- Supported file types
- Upload and storage
- Media serving configuration

### [PACKAGE_IMPORT.md](PACKAGE_IMPORT.md)
Automatic package import from DOC/DOCX files:
- Import process and pipeline
- Job statuses and error handling
- Document structure recognition
- Architecture and configuration

## Quick Start

### For Developers

1. Clone the repository
2. Start the database:
   ```powershell
   .\start-dev-db.ps1
   ```
3. Run the Blazor app from your IDE (F5)
4. See [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md) for details

### For DevOps

1. Set up VPS with Docker
2. Configure SSH keys and `.env` file
3. Push to `main` branch to trigger deployment
4. See [VPS_DEPLOYMENT.md](VPS_DEPLOYMENT.md) for details

## Project Structure

```
questions-hub/
├── QuestionsHub.Blazor/         # Main Blazor application
├── .github/workflows/           # CI/CD pipeline
├── db/                          # Database configuration
│   ├── dictionaries/            # Ukrainian FTS dictionary files
│   └── scripts/                 # SQL initialization scripts
├── docs/                        # Documentation (you are here)
├── media/                       # Sample media files
├── docker-compose.yml           # Docker Compose configuration
└── *.ps1                        # Helper scripts
```

## Key Files

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Defines all Docker services and profiles |
| `db/scripts/*.sql` | Database initialization scripts |
| `.env` (VPS only) | Production environment variables |
| `start-dev-db.ps1` | Start local development database |

