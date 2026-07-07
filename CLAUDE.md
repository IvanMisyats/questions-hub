# CLAUDE.md — Questions Hub

Online database of Ukrainian intellectual-game questions: "Що?Де?Коли?" (tours of numbered questions) and "Своя гра"/Shvager (themes of 5 questions valued 10–50). Every package has a `PackageType`. All UI is in **Ukrainian (uk-UA)**.

## Build & Run

```bash
dotnet restore && dotnet build --no-restore && dotnet test --no-build
# Local dev: .\start-dev-db.ps1 then run from IDE (https://localhost:5001)
# Migrations: cd QuestionsHub.Blazor && dotnet ef migrations add <Name> --output-dir Data/Migrations
```

## Tech Stack

C# 13, ASP.NET Core 10, Blazor Server, PostgreSQL 16 (Ukrainian FTS), EF Core, Bootstrap 5, Docker Compose, GitHub Actions CI/CD.

## Project Layout

- `QuestionsHub.Blazor/` — main app (Components, Controllers, Controllers/Api/V1, Data, Domain, Infrastructure, wwwroot)
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
- **Never hard-code the developer's real OS username in files** (paths, examples, comments) — use `/tmp`, an env var, or a placeholder like `<user>`

## Git Workflow

- **Always squash-merge**
- Do not push branches to remote
- Commit only on explicit command
- **Never amend commits that have been pushed to origin** — create a new commit instead

## Key Docs (read when needed)

| Doc | Content |
|-----|---------|
| `.github/copilot-instructions.md` | Full dev guidelines, domain model, common tasks |
| `docs/SITE_SPECIFICATION.md` | Complete feature spec (both game types), routes, UI details |
| `docs/SHVAGER_PLAN.md` | Своя гра (Shvager) feature: design decisions, subsystem designs, phased implementation plan |
| `docs/shvager.md` | Своя гра game rules and DOCX source-format reference |
| `docs/AUTHENTICATION.md` | Roles, access control, registration flow |
| `docs/PACKAGE_FORMAT.md` | `.qhub` interchange format schema |
| `docs/PACKAGE_IMPORT.md` | DOCX import pipeline |
| `docs/IMPORT_DEBUGGING.md` | Debugging imports: job artifacts, replaying the parser offline, numbering-cascade failure mode |
| `docs/LOCAL_DEV.md` | Headless run/verification: ports, dev DB via bash, curl smoke-testing (Cyrillic URLs, SSR encoding), bin locks |
| `docs/RESULTS.md` | Tournament results & question statistics: platform loaders (Rating/OpenQuiz), mapping rules, debugging |
| `docs/Stats_plan.md` | Results/statistics feature: design decisions and phased plan |
| `docs/SEARCH.md` | FTS implementation details |
| `docs/ICONS.md` | Icon system and available icons |
| `docs/BACKUPS.md` | Backup system overview, schedule, storage, IaC |
| `docs/API.md` | Public API reference: endpoints, auth, rate limits |
| `docs/CLOUDFLARE.md` | Cloudflare proxy settings, Blazor gotchas, origin protection |
