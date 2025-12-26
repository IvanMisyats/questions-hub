# GitHub Copilot Instructions

## Project Overview

This is the Questions Hub project - an online database of Ukrainian questions for the game "What? Where? When?".

### Key Documentation Files

- **README.md** - Describes the tech stack and technologies used in the project
- **AboutGame.md** - Contains detailed description of the project and the game

### Technology Stack

Refer to README.md for the complete technology stack. The project uses:
- Backend: C#, ASP.NET, Blazor
- Frontend: HTML, CSS, Bootstrap
- Database: PostgreSQL

## Development Guidelines

### Git Workflow

- **Always merge using `--squash` option**
- do not push branches to remote

### Code Style and Conventions

- Always write files as **UTF-8 without BOM** (no `EF BB BF` at the start).
- Follow C# naming conventions and .NET best practices
- Use async/await patterns for asynchronous operations
- Maintain consistent code formatting across the project

### Entity Framework Migrations

- Use Entity Framework Core for database migrations
- Test migrations locally before committing
- Always include migration rollback considerations

### Blazor Components

- Follow Blazor component best practices
- Keep components modular and reusable
- Use proper lifecycle methods and state management

### Database

- PostgreSQL is used as the primary database
- Use Npgsql provider for Entity Framework Core
- Follow proper indexing strategies for search functionality

## Project Structure

- `/QuestionsHub.Blazor/` - Main Blazor application
  - `/Components/` - Blazor components
  - `/Data/` - Database context and migrations
  - `/Domain/` - Domain models
  - `/wwwroot/` - Static assets

## Testing and Deployment

- Refer to documentation in `/docs/` folder for deployment information
- Test thoroughly before merging to main branch
- Ensure Docker configuration is properly maintained

## Additional Notes

- This is a work-in-progress project with planned future features
- Maintain backward compatibility when making database changes
- Document summary of AI changes in .github/upgrades/ folder. Do not commit files from .github/upgrades/ folder.

