# CLAUDE.md — Questions Hub

Online database of Ukrainian "Що?Де?Коли?" game questions. All UI is in **Ukrainian (uk-UA)**.

## Build & Run

```bash
dotnet restore && dotnet build --no-restore && dotnet test --no-build
# Local dev: .\start-dev-db.ps1 then run from IDE (https://localhost:5001)
# Migrations: cd QuestionsHub.Blazor && dotnet ef migrations add <Name> --output-dir Data/Migrations
```

## Tech Stack

C# 13, ASP.NET Core 10, Blazor Server, PostgreSQL 16 (Ukrainian FTS), EF Core, Bootstrap 5, Docker Compose, GitHub Actions CI/CD.

## Project Layout

- `QuestionsHub.Blazor/` — main app (Components, Controllers, Data, Domain, Infrastructure, wwwroot)
- `QuestionsHub.UnitTests/` — unit tests
- `db/` — PostgreSQL scripts and Ukrainian dictionary files
- `docs/` — detailed documentation (read on demand)

## Domain Model

**Package → Tours → (optional) Blocks → Questions**. Each entity has `OrderIndex` (0-based, ordering source of truth) and `Number` (display string). Key flags: `NumberingMode` (Global/PerTour/Manual), `SharedEditors`, `IsWarmup`, `AccessLevel` (All/RegisteredOnly/EditorsOnly), `PackageStatus` (Draft/Published/Archived).

## Testing

- **Always run related unit tests** (`dotnet test`) when modifying code in `QuestionsHub.Blazor/` that has corresponding tests in `QuestionsHub.UnitTests/`. Do not consider a change complete until tests pass.
- When adding new service methods or modifying existing ones, add or update unit tests.

## Code Conventions

- UTF-8 without BOM
- No `Async` suffix on method names
- Nullable reference types for optional fields
- **Prefer client-side JS over Blazor server roundtrips** for UI-only interactions
- Icons: `<Icon Name="check" Class="text-success" />` (SVG sprite in `wwwroot/icons.svg`)

## Git Workflow

- **Always squash-merge**
- Do not push branches to remote
- Commit only on explicit command
- **Never amend commits that have been pushed to origin** — create a new commit instead

## Key Docs (read when needed)

| Doc | Content |
|-----|---------|
| `.github/copilot-instructions.md` | Full dev guidelines, domain model, common tasks |
| `docs/SITE_SPECIFICATION.md` | Complete feature spec, routes, UI details |
| `docs/AUTHENTICATION.md` | Roles, access control, registration flow |
| `docs/PACKAGE_FORMAT.md` | `.qhub` interchange format schema |
| `docs/PACKAGE_IMPORT.md` | DOCX import pipeline |
| `docs/SEARCH.md` | FTS implementation details |
| `docs/ICONS.md` | Icon system and available icons |
| `docs/BACKUPS.md` | Backup system overview, schedule, storage, IaC |
| `docs/CLOUDFLARE.md` | Cloudflare proxy settings, Blazor gotchas, origin protection |
