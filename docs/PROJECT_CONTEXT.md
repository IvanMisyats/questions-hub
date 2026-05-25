---
project_name: 'questions-hub'
user_name: 'Johny'
date: '2026-05-25'
sections_completed:
  ['technology_stack', 'language_rules', 'framework_rules', 'testing_rules', 'quality_rules', 'workflow_rules', 'anti_patterns']
status: 'complete'
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

**Runtime / language**
- `net10.0` (TargetFramework) — C# 13, ASP.NET Core 10, Blazor Server
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` on both projects
- `InternalsVisibleTo` is set from `QuestionsHub.Blazor` → `QuestionsHub.UnitTests` (tests may access `internal`)

**Key NuGet packages (exact versions in csproj — do not bump without explicit ask)**
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.1
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0
- `Microsoft.EntityFrameworkCore.Design` 10.0.0
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.5
- `DocumentFormat.OpenXml` 3.2.0 (DOCX import pipeline)
- `Mailjet.Api` 3.0.1 (transactional email)

**Test stack (do not swap libraries)**
- `xunit` 2.6.2 + `xunit.runner.visualstudio` 2.5.4 + `xunit.analyzers` 1.8.0
- `FluentAssertions` 6.12.0 (assertions style — use `Should().Be(...)`, not `Assert.Equal`)
- `Microsoft.EntityFrameworkCore.InMemory` 10.0.0 (in-memory DbContext for tests)

**Database**
- PostgreSQL 16 with Ukrainian hunspell FTS dictionary (in `db/`)
- FTS uses generated columns `SearchVector` + `SearchTextNorm`; never compute these in app code

**CI/CD**
- GitHub Actions: `actions/checkout@v5`, `actions/setup-dotnet@v5` with `dotnet-version: '10.0.x'`, `actions/cache@v5`
- CI runs `dotnet restore && dotnet build --no-restore && dotnet test --no-build` on Ubuntu

**Locale**
- All UI text and user-visible error messages in **Ukrainian (uk-UA)** — including service-returned error strings like `"Пакет не знайдено."`

## Critical Implementation Rules

### Language-Specific Rules (C# / .NET 10)

- **No `Async` suffix** on method names — even when returning `Task`/`Task<T>`. E.g. `DeletePackage`, not `DeletePackageAsync`. This is a project-wide deviation from the common .NET convention.
- **Nullable reference types are enabled** — annotate optional fields with `string?`/`T?`, never use the `null-forgiving !` operator to "fix" a warning; rework the signature instead.
- **UTF-8 without BOM** for all new files (source, JSON, markdown, SQL).
- **`ImplicitUsings` is on** — do not add redundant `using System;`, `System.Linq`, `System.Threading.Tasks`, `Microsoft.EntityFrameworkCore` if already implicit.
- **File-scoped namespaces** (`namespace QuestionsHub.Blazor.Infrastructure;`) — match the existing style, do not introduce block-scoped namespaces.
- **DbContext is created via factory**, not injected directly:
  ```csharp
  await using var context = await _dbContextFactory.CreateDbContextAsync();
  ```
  Blazor Server components live longer than a single EF unit of work. Never inject `QuestionsHubDbContext` directly — always inject `IDbContextFactory<QuestionsHubDbContext>`.
- **Constructor injection only** — no service locator, no `[Inject]` on plain C# classes. `[Inject]` is for Razor components.
- **Logging** — use `ILogger<T>` with structured templates (`_logger.LogInformation("Deleting package {PackageId}", id);`), never string interpolation in the message.
- **Result objects over exceptions** for expected user-facing failures — services return small `record` results (e.g. `DeleteResult(bool Success, string Message)`) with Ukrainian messages, instead of throwing.
- **Apostrophe normalization** — Ukrainian text uses ' (U+02BC) but users type ' (U+0027); see `NormalizeApostrophes` migration and related helpers. When comparing/searching text, normalize first.

### Framework-Specific Rules (Blazor Server + EF Core)

**Blazor**
- **Prefer client-side JS over server roundtrips** for UI-only behavior (focus, scroll, clipboard, DOM, validation hints). Place reusable JS in `wwwroot/*.js` and call via `IJSRuntime`.
- Use `@rendermode InteractiveServer` only on components that actually need interactivity; leave the rest static SSR (cheaper, fewer circuit issues).
- Components annotated with `@attribute [Authorize(Roles = "...")]` — the role names in use are `User`, `Editor`, `Admin`. Anonymous users see only published, `AccessLevel=All` packages.
- Use the `<Icon Name="..." />` component (SVG sprite at `wwwroot/icons.svg`) — never inline raw `<svg>` or pull from an icon font.
- All Bootstrap markup uses Bootstrap 5 classes (`btn btn-primary`, `mb-3`, etc.) — do not import a CSS framework alongside.

**EF Core**
- Schema changes **require a migration**: `cd QuestionsHub.Blazor && dotnet ef migrations add <Name> --output-dir Data/Migrations`. Never hand-edit a previous migration that has been committed.
- Migrations must be **rollback-safe** (provide a working `Down`) and **backward-compatible** with the previous app version (deploys are not synchronized with code).
- `OrderIndex` is the ordering source of truth; `Number` is the display label. Renumbering and reordering always goes through `PackageRenumberingService` — never mutate `Number` directly.
- Access control flows through `AccessControlService`. Do not re-implement permission checks in components or controllers; call `CanView`, `CanEdit`, `CanDeletePackage`, etc.
- Eager-load with explicit `Include`/`ThenInclude`; **lazy loading is not enabled** — a missing `Include` means a silent `null` collection, not a query.
- Generated columns (`SearchVector`, `SearchTextNorm`) — read-only from app code, never assigned.

**API (public read-only)**
- New API endpoints go under `Controllers/Api/V1/`, return DTOs from `Controllers/Api/V1/Dto/`, and authenticate via the `X-API-Key` header (`ApiKeyAuthenticationHandler`).
- API surface is **anonymous-context only** — visibility filtering must match what an anonymous web visitor would see (`Published` + `AccessLevel.All`). Never expose a package, question, or field that requires login.
- Rate limits live at two layers (per-key in ASP.NET, per-IP in nginx). When adding endpoints, register them in the rate limiter policy and document the limit in `docs/API.md`.

### Testing Rules

- **Always run `dotnet test`** after touching any file in `QuestionsHub.Blazor/` that has a corresponding test in `QuestionsHub.UnitTests/`. A change is not done until tests pass.
- **Add or update unit tests** when adding/modifying a service method. Service tests live next to their target name (e.g. `PackageRenumberingServiceTests.cs`, `AuthorServiceTests.cs`).
- **xUnit + FluentAssertions** is the only style used:
  ```csharp
  result.Should().NotBeNull();
  result.Success.Should().BeTrue();
  ```
  Do not introduce `Assert.*`, NUnit, or Moq idioms.
- **In-memory DbContext** via `InMemoryDbContextFactory` for service tests; **do not** mock `QuestionsHubDbContext` or `IDbContextFactory<>`. The InMemory provider is the project's mock.
- **Golden-file tests** drive the import pipeline (`ImportParsing/Golden/PackageGoldenTests.cs`). Inputs under `TestData/` are copied to output (`PreserveNewest`). When a parser change is intentional, regenerate the golden file rather than fudging assertions.
- **Test naming**: `MethodName_Scenario_ExpectedOutcome` or `Should_Do_X_When_Y`. Files end in `Tests.cs`.
- **Test fixtures use `internal`** when possible — `InternalsVisibleTo` is wired so tests can reach internal types without making them public.
- **No integration tests** against a live Postgres in CI — keep DB-touching tests on InMemory. Real-Postgres validation is manual via `start-dev-db.ps1`.

### Code Quality & Style Rules

**File and folder layout**
- `Components/` — Razor (Pages, Layout, Account, shared components). Page routes live in `Components/Pages/{Admin|Account|...}`.
- `Controllers/` — MVC/API controllers. Public API is segregated under `Controllers/Api/V1/`; DTOs under `Controllers/Api/V1/Dto/` and `Controllers/Dto/`.
- `Data/` — `DbContext`, migrations (`Data/Migrations/`), and seed data.
- `Domain/` — POCO entities and enums only. No EF Core attributes here unless unavoidable; configure via Fluent API in `DbContext`.
- `Infrastructure/` — services, grouped by concern (`Auth/`, `Email/`, `Import/`, `Media/`, `Search/`, `Api/`, `Telegram/`, `Export/`). One service per file unless it's a partial class.
- `Utils/` — small static helpers (`UkrainianNameHelper`, `DateFormatter`, `PlayedPeriodFormatter`). Pure functions, no DI.
- `wwwroot/` — static assets; `icons.svg` is the icon sprite; reusable JS lives at `wwwroot/*.js`.

**Naming**
- Types: `PascalCase`. Interfaces start with `I`. Async methods do **not** end in `Async`.
- Private fields: `_camelCase`. Locals and parameters: `camelCase`.
- Razor pages: `PascalCase.razor`, one route per file.
- Migrations: `YYYYMMDDHHMMSS_DescriptiveName.cs` (auto-generated; pick a descriptive `<Name>`).
- Service classes: end in `Service` (`PackageService`, `SearchService`, `ApiKeyService`).
- Partial-class parsers are split by feature suffix: `PackageParser.QuestionParsing.cs`, `PackageParser.TourParsing.cs`, etc. Continue the pattern — do not collapse them into one file.

**Documentation**
- Use XML doc comments (`/// <summary>...`) for **public service methods**, especially ones returning result records. Skip them for trivial helpers and private members.
- Inline comments only when the *why* is non-obvious. Do not narrate the *what*.
- Markdown docs in `docs/` are authoritative for features. When changing a domain area, update the corresponding doc in the same PR (`AUTHENTICATION.md`, `SEARCH.md`, `API.md`, `PACKAGE_FORMAT.md`, etc.).

**Configuration**
- `appsettings.json` for defaults, `appsettings.Development.json` for local overrides. **No secrets in either** — use `dotnet user-secrets` (UserSecretsId is set) locally and env vars in production.
- Strongly-typed options via `IOptions<T>` bound from configuration sections (`MediaUploadOptions`, `MediaSecurityOptions`, `EmailSettings`, `TelegramSettings`, `PackageImportOptions`).

### Development Workflow Rules

**Git**
- **Always squash-merge.** No merge commits in `main`.
- **Never amend a commit that has been pushed to origin.** Add a new commit instead.
- **Do not push branches to remote** unless the user explicitly asks.
- **Commit only on explicit command.** Do not auto-commit after each successful task.
- Stage files by name (`git add path/to/file`) — avoid `git add -A` / `git add .` (uploads/ and notes/ folders are full of generated artifacts).
- Branch naming is informal; no enforced prefix. PRs go against `main`.

**Local dev**
```bash
.\start-dev-db.ps1                                          # Postgres via docker-compose
dotnet restore && dotnet build --no-restore && dotnet test --no-build
# Run from IDE → https://localhost:5001
cd QuestionsHub.Blazor && dotnet ef migrations add <Name> --output-dir Data/Migrations
```

**CI/CD**
- `ci.yml` runs build + test on every push and PR to `main`. A red CI means the change is not ready.
- `cd.yml` deploys to the VPS on merge to `main`. Database migrations apply automatically on app start — keep them backward-compatible so a partial rollout never breaks the running pod.

**Change log / planning notes**
- Implementation notes, plans, and post-mortems go in `.github/upgrades/` — treat as scratch unless told otherwise.
- BMad planning artifacts go under `_bmad-output/`. Do not commit by default.

**Don't touch without permission**
- `uploads/` — user-uploaded media and import jobs (not source).
- `db/dictionaries/` — Ukrainian hunspell dictionary; updates are coordinated.
- `Data/Migrations/*` for any migration already in `main`.

### Critical Don't-Miss Rules

**Anti-patterns to avoid**
- ❌ Injecting `QuestionsHubDbContext` directly into a Blazor component or long-lived service. Always use `IDbContextFactory<QuestionsHubDbContext>` and dispose the context per operation.
- ❌ Mutating `Question.Number` or `Tour.Number` outside `PackageRenumberingService`. The display label is derived from `OrderIndex` + `NumberingMode`; bypassing the service desyncs the package.
- ❌ Computing or assigning `SearchVector` / `SearchTextNorm` from C#. These are Postgres generated columns — read-only.
- ❌ Hand-editing a previously committed migration. Add a new migration that performs the fix-up.
- ❌ Re-implementing authorization in a component or controller. Always call `AccessControlService`.
- ❌ Returning English error strings to users. All user-visible messages are in Ukrainian (`"Пакет не знайдено."`, `"Ви не маєте прав..."`).
- ❌ Adding endpoints under `Controllers/Api/V1/` that surface data an anonymous web visitor couldn't see. The API is anonymous-context only.
- ❌ Mocking `DbContext`, `DbSet`, or `IDbContextFactory` in tests. Use the InMemory provider via `InMemoryDbContextFactory`.
- ❌ Spinning the server with `@onchange` for ephemeral UI state (focus, validation feedback, scroll). Do it in JS via `wwwroot/*.js` + `IJSRuntime`.

**Security must-dos**
- API keys (`Domain/ApiClient.cs`, `ApiKeyService`) are stored hashed. Never log raw keys; show them once at issue time only.
- File uploads pass through `MediaService` with extension/content-type/size enforced by `MediaSecurityOptions`. Never write to `uploads/` from anywhere else.
- DOCX import is sandboxed in a per-job `uploads/jobs/<guid>/working/` folder. Treat the parsed JSON (`extracted.json`, `package_import.json`) as **untrusted input** when reasoning about XSS / SSRF.
- HTML/text rendered from question content (especially `SourceLinkifier` output) must be sanitized — there is a known XSS hardening item tracked in postponed security fixes. Do not introduce new `MarkupString` usages without sanitization.
- Email sending goes through `MailjetEmailSender`. Never log full message bodies (they may contain reset tokens).

**Domain edge cases**
- A package may have `Blocks` between `Tour` and `Question` (rare). Code walking the hierarchy must handle both `tour.Questions` directly and `tour.Blocks[].Questions`.
- Warmup tour (`IsWarmup`/`TourType.Warmup`) is numbered `"0"` and rendered first, regardless of `OrderIndex`.
- `SharedEditors=true` means editors live on the Package; `false` means they're computed from tour/block editors. Code that lists "who can edit X" must branch on this.
- `AccessLevel` ∈ {`All`, `RegisteredOnly`, `EditorsOnly`} and `PackageStatus` ∈ {`Draft`, `Published`, `Archived`} — both must be checked together to determine visibility.
- Apostrophes: search/comparison code must normalize `'` (U+0027) ↔ `'` (U+02BC) — see `TextNormalizer` and the `NormalizeApostrophes` migration.

**Performance gotchas**
- Blazor Server holds a SignalR circuit per user; large interactive components retain state in memory. Prefer static SSR + JS for read-heavy pages (package list, search results).
- `Include`/`ThenInclude` chains across the full hierarchy (`Package → Tours → Blocks → Questions → Authors`) can fan out badly; for list views, project to a DTO instead.
- FTS queries against `SearchVector` need the correct config name (Ukrainian); use `SearchService` rather than handcrafting `tsquery`.

---

## Usage Guidelines

**For AI Agents:**
- Read this file before implementing any code in `questions-hub`.
- Follow all rules exactly. When in doubt, choose the more restrictive option.
- Treat the bulleted "must / never" lines as hard constraints, not suggestions.
- If you discover a new project-specific rule worth keeping, propose adding it here.

**For Humans:**
- Keep this file lean and focused on rules agents actually need.
- Update when the tech stack or domain model shifts (new entity, new auth role, new API surface).
- Review periodically; remove rules that become self-evident from the codebase.

Last Updated: 2026-05-25
