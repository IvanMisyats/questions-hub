# GitHub Copilot Instructions

## Project Overview

**Questions Hub** (База українських запитань "Що?Де?Коли?") is an online database of Ukrainian questions for the intellectual game "What? Where? When?" (Що?Де?Коли?).

**Purpose**: Provide a structured, searchable repository of game questions for players, editors, and game organizers in Ukraine.

**Language**: Ukrainian (uk-UA) - all UI text, dates, and user-facing content should be in Ukrainian.

### Key Documentation

| Document | Purpose |
|----------|---------|
| `docs/SITE_SPECIFICATION.md` | **Complete feature specification** - domain model, implemented features, UI/UX details |
| `AboutGame.md` | Game rules and terminology in Ukrainian |
| `README.md` | Tech stack and quick start guide |
| `docs/LOCAL_DEVELOPMENT.md` | Local development setup |

---

## Domain Model (Essential Context)

The application manages a hierarchy of entities for game question packages:

### Entity Hierarchy

```
Package (Пакет)
├── Tours (Тури) - rounds within a package
│   ├── Blocks (Блоки) - optional groupings within tours (rare)
│   │   └── Questions (Запитання)
│   └── Questions (Запитання) - can exist directly under tour or in blocks
└── PackageEditors - when SharedEditors=true
```

### Core Entities

| Entity | Ukrainian | Description |
|--------|-----------|-------------|
| `Package` | Пакет | Collection of questions for a tournament |
| `Tour` | Тур | Round within a package (typically 12 questions) |
| `Block` | Блок | Optional grouping within a tour (has own editors) |
| `Question` | Запитання | Single question with answer and metadata |
| `Author` | Автор/Редактор | Person who creates questions or edits tours |
| `ApplicationUser` | Користувач | Registered user account |

### Key Domain Concepts

- **OrderIndex**: 0-based physical ordering (source of truth for display order)
- **Number**: Display number (string, can be "1", "0", "F" for hex, etc.)
- **NumberingMode**: Global (across package), PerTour (restart each tour), Manual
- **SharedEditors**: When true, editors defined at package level; when false, computed from tour/block editors
- **IsWarmup**: Tour flag - warmup tour is numbered "0" and appears first
- **AccessLevel**: All, RegisteredOnly, EditorsOnly - controls package visibility
- **PackageStatus**: Draft, Published, Archived

### Question Structure

A question contains:
- `HostInstructions` (Вказівка ведучому) - not read to players
- `HandoutText`/`HandoutUrl` (Роздатка) - distributed to teams
- `Text` (Текст запитання) - the main question
- `Answer` (Відповідь) - correct answer
- `AcceptedAnswers` (Залік) - alternative accepted answers
- `RejectedAnswers` (Незалік) - explicitly rejected answers
- `Comment` (Коментар) - explanation
- `Source` (Джерело) - references
- `Authors` - question creators

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Backend | C# 13, ASP.NET Core 10, Blazor Server |
| Frontend | HTML, CSS, Bootstrap 5, Blazor Components |
| Database | PostgreSQL 16 with Ukrainian FTS (hunspell) |
| ORM | Entity Framework Core with Npgsql |
| Auth | ASP.NET Core Identity |
| Container | Docker, Docker Compose |
| Email | Mailjet |

---

## Project Structure

```
/QuestionsHub.Blazor/
├── Components/
│   ├── Account/           # Auth pages (Login, Register, Profile)
│   ├── Layout/            # MainLayout, NavMenu, TopSearchBar
│   └── Pages/
│       ├── Admin/         # User/editor management
│       ├── Home.razor     # Package list
│       ├── PackageDetail.razor    # View package
│       ├── ManagePackages.razor   # Package list (editors)
│       ├── ManagePackageDetail.razor  # Package editor
│       ├── EditorProfile.razor    # Author profile
│       └── Search.razor   # Full-text search
├── Controllers/           # API (Auth, Media)
├── Data/                  # DbContext, migrations, seeding
├── Domain/                # Entity models
├── Infrastructure/        # Services (Search, Package, Media, etc.)
└── wwwroot/
    ├── app.css            # Global styles
    └── icons.svg          # SVG sprite for icons
```

### Key Services

| Service | Purpose |
|---------|---------|
| `PackageService` | Package CRUD operations |
| `PackageManagementService` | Package editing logic |
| `PackageRenumberingService` | Question numbering logic |
| `SearchService` | Full-text search with Ukrainian morphology |
| `AuthorService` | Author management |
| `AccessControlService` | Package access authorization |
| `MediaService` | Image/video/audio upload |

---

## Development Guidelines

### Git Workflow

- **Always merge using `--squash` option**
- Do not push branches to remote
- Commit changes only on explicit command

### Code Style

- Write files as **UTF-8 without BOM**
- Follow C# naming conventions and .NET best practices
- Use async/await for async operations; **no `Async` suffix** on method names
- Prefer readable code over clever solutions
- Use nullable reference types (`string?` for optional fields)

### Blazor Components

- **Prefer client-side JavaScript over Blazor server roundtrips** for:
  - UI-only interactions (focus, scroll, clipboard, DOM manipulation)
  - Form field pre-filling and validation feedback
- Extract reusable JS to `/wwwroot/*.js` files
- Use `@onchange` with server roundtrip only when data must be persisted
- Use `@rendermode InteractiveServer` for interactive components

### Icons

Use the `Icon.razor` component with SVG sprite:
```razor
<Icon Name="check" Class="text-success" />
```
Available icons: check, link, drag, search, close, group, shield-exclamation, person-plus, person-minus

### Database

- PostgreSQL with Ukrainian FTS (hunspell dictionary)
- Use migrations: `dotnet ef migrations add MigrationName`
- Test migrations locally before committing
- Consider rollback in migration design
- Full-text search uses `SearchVector` (generated column) and `SearchTextNorm`

### Authorization

| Role | Capabilities |
|------|--------------|
| Anonymous | View published packages |
| User | + Edit own profile |
| Editor | + Create/edit own packages |
| Admin | + Manage all packages and users |

---

## Common Tasks Reference

### Adding a New Field to Question

1. Add property to `Domain/Question.cs`
2. Create migration: `dotnet ef migrations add AddFieldName`
3. Update `QuestionCard.razor` for display
4. Update question editor modal in `ManagePackageDetail.razor`

### Adding a New Page

1. Create `Components/Pages/NewPage.razor`
2. Add `@page "/route"` directive
3. Add navigation link in `NavMenu.razor` if needed
4. Add authorization with `@attribute [Authorize(Roles = "...")]`

### Working with Package Hierarchy

- Tours ordered by `OrderIndex`, display `Number`
- Questions ordered by `OrderIndex` within tour
- Blocks optional; when present, questions have `BlockId`
- Use `PackageRenumberingService` after reordering

---

## Testing

- Unit tests in `/QuestionsHub.UnitTests/`
- Run: `dotnet test`
- Test files follow pattern `*Tests.cs`

---

## Additional Notes

- Maintain backward compatibility when changing database schema
- Document AI changes summary in `.github/upgrades/` folder (do not commit these files)
- Check `docs/SITE_SPECIFICATION.md` for detailed feature specifications

