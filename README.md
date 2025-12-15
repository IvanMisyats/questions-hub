# Questions Hub
This project is a online site of the database of Ukrainian questions of game "What?Where?When?".

The main functionality includes:
* structured storage of tournaments, packages and questions with navigation between them.
* efficient search by text over the questions
* adding new packages into database by uploading word/pdf document
* role system to limit access to packages

Future functionality which might be added:
* playing questions in interactive mode with timer
* uploading and storing the results of tournaments
* adding comments/ratings to questions

## Technologies Used

* Backend: C#, ASP.NET, Blazor
* Frontend: HTML, CSS, Bootstrap
* DB: PostgreSQL

## Documentation

- **[Local Development Guide](docs/LOCAL_DEVELOPMENT.md)** - Set up local development environment
- **[CI/CD Testing Guide](docs/LOCAL_TESTING.md)** - Test GitHub Actions pipeline locally
- **[VPS Setup Guide](docs/LOCAL_TESTING.md#vps-setup-requirements)** - Configure production VPS

## Setup and Run

### Prerequisites

- [Docker](https://www.docker.com/get-started) and Docker Compose
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (for local development without Docker)
- Git

### Local Development (Windows)

#### 1. Clone the Repository

```bash
git clone https://github.com/IvanMisyats/questions-hub.git
cd questions-hub
```

#### 2. Configure Environment Variables

Create a `.env` file from the example:

```powershell
cp .env.example .env
```

Edit `.env` and set your passwords:

```env
POSTGRES_ROOT_PASSWORD=your_local_root_password
QUESTIONSHUB_PASSWORD=your_local_app_password
POSTGRES_DATA_PATH=./postgres_data
```

#### 3. Start with Docker Compose

```powershell
# Build and start all services (web app + PostgreSQL)
docker-compose up -d

# Check logs to verify successful startup
docker logs questions-hub-web
docker logs questions-hub-db
```

#### 4. Access the Application

Open your browser and navigate to: `http://localhost:8080`

#### 5. Database Management

**Check database status:**
```powershell
.\check-db-location.ps1
```

**Clean database (deletes all data):**
```powershell
.\cleanup-db.ps1
```

**Manual cleanup:**
```powershell
docker-compose down
Remove-Item -Recurse -Force .\postgres_data
docker-compose up -d
```

### Local Development (Linux/Mac)

#### 1. Clone and Configure

```bash
git clone https://github.com/IvanMisyats/questions-hub.git
cd questions-hub

# Create .env file
cp .env.example .env
# Edit .env with your passwords
```

#### 2. Start Services

```bash
# Build and start
docker-compose up -d

# Check logs
docker logs questions-hub-web
docker logs questions-hub-db
```

#### 3. Clean Database

```bash
docker-compose down
rm -rf ./postgres_data
docker-compose up -d
```

### Development without Docker

#### 1. Install PostgreSQL

Install PostgreSQL 16 locally and create a database named `questionshub`.

#### 2. Update Connection String

Edit `QuestionsHub.Blazor/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=questionshub;Username=postgres;Password=your_password"
  }
}
```

#### 3. Run Migrations

```bash
cd QuestionsHub.Blazor
dotnet ef database update
```

#### 4. Run the Application

```bash
dotnet run
```

Navigate to `http://localhost:5000`

### Useful Commands

| Command | Description |
|---------|-------------|
| `docker-compose up -d` | Start all services in background |
| `docker-compose down` | Stop all services |
| `docker-compose logs -f` | View logs (follow mode) |
| `docker logs questions-hub-web` | View web app logs |
| `docker logs questions-hub-db` | View database logs |
| `docker-compose restart` | Restart all services |
| `docker-compose build --no-cache` | Rebuild images from scratch |
| `.\check-db-location.ps1` | Check database status (Windows) |
| `.\cleanup-db.ps1` | Clean database with confirmation (Windows) |

### Database Location

- **Local Windows**: `questions-hub\postgres_data\`
- **Local Linux/Mac**: `./postgres_data/`
- **VPS**: `/home/github-actions/questions-hub-data/postgres_data`

The database files are stored outside Docker containers for persistence.

### Troubleshooting

**Port already in use:**
```bash
# Change ports in docker-compose.yml or stop conflicting services
docker-compose down
# Edit docker-compose.yml to use different ports
docker-compose up -d
```

**Permission errors:**
```bash
# Run with elevated privileges
sudo docker-compose up -d  # Linux/Mac
# Run PowerShell as Administrator (Windows)
```

**Database connection errors:**
```bash
# Check if PostgreSQL container is healthy
docker ps
# View database logs
docker logs questions-hub-db
# Restart services
docker-compose restart
```

**Clean slate (delete everything):**
```bash
docker-compose down -v
docker system prune -a
# Delete postgres_data folder
# Then: docker-compose up -d
```

## Hosting & Deployment

The application is hosted on a VPS from OVH with the following specs:
- **vCore**: 1
- **RAM**: 2 GB
- **Storage**: 20 GB
- **Deployment**: Automated via GitHub Actions CI/CD

### Deployment Architecture

**VPS Setup:**
- Docker containers run directly via `docker run` (no docker-compose on VPS)
- Deployment user: `github-actions` (minimal privileges, no sudo)
- Application directory: `/home/github-actions/questions-hub`
- Database storage: `/home/github-actions/questions-hub-data/postgres_data`
- Network: Custom Docker network `questions-hub-network` for container communication

**CI/CD Pipeline:**
1. Build .NET application and run tests
2. Build Docker image from source
3. Push image to GitHub Container Registry (GHCR)
4. SSH to VPS and pull latest image
5. Deploy using `docker run` commands for both PostgreSQL and web app

**Security:**
- Database passwords stored in `.env` file on VPS (not in GitHub secrets)
- SSH key authentication for deployment
- Containers run with memory limits
- Non-root deployment user

