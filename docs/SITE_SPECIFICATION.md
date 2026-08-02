# Questions Hub - Site Specification

## Overview

**Questions Hub** (База українських запитань) is an online database of Ukrainian questions for intellectual games. It hosts packages of two game types:

- **Що?Де?Коли? (ЩДК / WWW)** — team game; packages consist of tours of sequentially numbered questions.
- **Своя гра (Shvager / свояк)** — buzzer game for 3–4 players; packages consist of themes of 5 questions valued 10–50. Game rules and source-format reference: [shvager.md](shvager.md).

**Purpose**: Provide a structured, searchable repository of game questions for players, editors, and game organizers in Ukraine.

**Target Audience**:
- Players and teams who want to practice and study past questions
- Game editors who create new question packages
- Tournament organizers who need access to question archives
- Anyone interested in the Ukrainian intellectual gaming community

**Language**: Ukrainian (uk-UA)

**Branding**: Site name is game-neutral — «База українських запитань» (top bar, page titles, emails); nav sidebar brand is «База запитань». Game names appear as type labels, tabs, and badges only.

**Last Updated**: July 2, 2026

---

## Game Types

Every package has exactly one type, chosen at creation/import and immutable afterwards. It must always be obvious which type of package/question the user is looking at — every package card, package page, question card, and search result carries a type badge (ЩДК / Своя гра).

| Aspect | Що?Де?Коли? | Своя гра |
|---|---|---|
| Package children | Tours (Тури) | Themes (Теми) |
| Container display | «Тур 1», «Розминка», «Перестрілка» | «Тема 1. {Назва}» — themes have titles |
| Optional sub-grouping | Blocks (Блоки) | — (not applicable) |
| Questions per container | Any number | Canonically 5 |
| Question identifier | Number («1», «2», … per NumberingMode) | Value («10»–«50»), derived from position |
| Numbering modes | Global / PerTour / Manual | Not applicable (values are fixed by position) |
| Special tours | Warmup (Розминка), Shootout (Перестрілка) | Not applicable |
| Question fields | Text, Answer, Залік, Незалік, Коментар, Джерело, Вказівка ведучому, handouts | Same minus Вказівка ведучому, plus **Форма** (answer-form hint) |
| Import source | DOCX (tour-based formats), .qhub | DOCX (theme-based formats), .qhub |

Both types share: statuses (Draft/Published/Archived), access levels, owners, editors/authors, tags (including 18+), media handling, search infrastructure, and the public API.

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Backend | C#, ASP.NET Core, Blazor Server |
| Frontend | HTML, CSS, Bootstrap, Blazor Components |
| Database | PostgreSQL with Entity Framework Core (Ukrainian FTS: hunspell dictionary, tsvector, pg_trgm) |
| Authentication | ASP.NET Core Identity (+ API keys for public API) |
| Containerization | Docker, Docker Compose |

---

## Domain Model

### Core Entities

- **Package (Пакет запитань)** - A collection of questions prepared for a tournament or a game
  - Has **Type**: `Www` or `Shvager` — the game type discriminator; set at creation/import, never changed
  - Has owner (User who created it)
  - Has status: Draft, Published, or Archived
  - Has AccessLevel: All (default), RegisteredOnly, or EditorsOnly - controls who can view the package
  - Has optional Preamble (Преамбула) - info from editors, usually contains testers list
  - Has NumberingMode: Global / PerTour / Manual — **ЩДК only**; ignored (and hidden in UI) for Shvager packages
  - Has SharedEditors flag: when true, editors are defined at package level; when false, computed from tour/block editors (applies to both types)
  - Has Tags (many-to-many), including the special 18+ tag that blurs content
- **Tour (Тур / Тема)** - A round within a ЩДК package, or a theme within a Shvager package
  - Has OrderIndex (0-based, source of truth for order) and Number for display
  - Has optional **Title** — the theme name; used by Shvager (required for a complete theme), null for ЩДК
  - Has Type: Regular / Warmup / Shootout — **ЩДК only**; Shvager themes are always Regular
  - Has optional Preamble; has many-to-many Editors (for Shvager these are the theme authors)
  - Can optionally contain Blocks — **ЩДК only**
- **Block (Блок)** - Optional grouping within a ЩДК tour (rare feature). Not applicable to Shvager.
- **Question (Запитання)** - A single question
  - Has OrderIndex (0-based, unique within tour) and Number for display
    - ЩДК: Number assigned per package NumberingMode
    - Shvager: Number holds the **value** — «10»/«20»/«30»/«40»/«50», auto-derived from position within the theme: `(OrderIndex + 1) × 10`
  - Has Text and Answer (required); optional Залік (AcceptedAnswers), Незалік (RejectedAnswers), Comment, Source, handout text/media, comment attachment
  - Has optional HostInstructions (Вказівка ведучому) — ЩДК only
  - Has optional **AnswerForm (Форма)** — answer-form hint (e.g. «таку назву», «цих предметів») — Shvager only
  - Has many-to-many Authors
  - Has DB-generated search columns (SearchTextNorm, SearchVector) — identical for both types
- **Author (Автор/Редактор)** - A person who creates questions or edits tours/themes
  - Unique on (FirstName, LastName); can be linked to a User account
  - Statistics are tracked **per game type** (see Authors pages)
- **User (Користувач)** - Application user with profile information

---

## Implemented Features

### 1. Home Page (Головна сторінка)
**Route**: `/`

Two **type tabs** above the content: «Що?Де?Коли?» (default) and «Своя гра». The active tab persists in the query string (`?type=shvager`) so views are shareable. Each tab shows:

- Popular-tag pill row and filter panel (package title search, editor dropdown, sort by publication/play date) — filters apply within the active tab
- Total count for the active type
- Responsive card grid of published packages (24/page, paginated), ordered by play date (newest first)

Package cards carry a type badge. ЩДК cards show question count («N запитань»); Shvager cards show theme and question counts («N тем · M запитань»).

### 2. Package Detail Page (Сторінка пакету)
**Route**: `/package/{id}`

Single scrollable page with the full package; a prominent type badge sits next to the title. Left sidebar (TourNavigation) provides quick navigation with smooth scrolling. «Показати всі відповіді» toggles all answers.

**ЩДК rendering**: tours headed «Розминка» / «Тур N» / «Перестрілка», optional blocks, questions headed «Запитання N».

**Shvager rendering**: themes headed «Тема N. {Назва}» with theme authors and preamble; questions headed by their **value badge** (10/20/30/40/50). The sidebar lists theme titles.

### 3. Question Card Component

Displays a question with (if present) host instructions, handout materials (text, images, video, audio), and the question text. A toggle reveals the spoilered answer section: Відповідь, Залік, Незалік, **Форма** (Shvager), Коментар, comment attachment, Джерело (auto-linkified), Автор(и). 18+ packages blur content until revealed (per-package, remembered client-side).

### 4. Question Permalink (Сторінка запитання)
**Route**: `/question/{id}`

Single-question card with breadcrumb: ЩДК — «{Пакет} · Тур N · Запитання M»; Shvager — «{Пакет} · Тема «{Назва}» · {value}». Carries the type badge.

### 5. Search (Пошук)
**Route**: `/search` or `/search/{query}`

Full-text search across all published questions of **both game types** with Ukrainian morphology support.

**Type filter**: three-state toggle above the results — «Усі» (default) / «Що?Де?Коли?» / «Своя гра», persisted in the query string (`?type=`).

**Features**:
- **Ukrainian Morphology** - Finds words in different forms (відмінки, роди, числа)
- **Accent Insensitive** - Searches ignore Ukrainian accents (А́мундсен = Амундсен)
- **Typo Tolerance** - Finds results even with spelling mistakes (via trigram matching)
- **Result Highlighting** - Matched words highlighted with `<mark>` tags

**Search Syntax**: `слово1 слово2` (AND), `слово1 OR слово2`, `"точна фраза"`, `-слово` (exclude).

**Searchable Fields**: Question Text, Handout Text, Answer, Accepted/Rejected Answers, Comment. (Theme titles and Форма are **displayed** in Shvager result cards but not matched by the query — possible future upgrade.)

**Result cards**: type badge + context line — ЩДК: «{Пакет} · Тур N · Запитання M»; Shvager: «{Пакет} · Тема «{Назва}» · {value}» — plus the question content with highlights and a link to the question's anchor in the package page.

### 6. Authors List (Автори)
**Route**: `/editors` (paginated)

Public list of all authors with **per-type statistics** — separate columns for ЩДК (packages, questions) and Своя гра (packages, questions), ranked by total question count. Only content from published packages is counted.

### 7. Author Profile (Профіль автора)
**Route**: `/editor/{id}`

Author name, linked-user info, and per-type statistics. Packages the author edited are grouped **by game type** into separate sections; within ЩДК packages with per-tour editors, tour/block links are shown («Пакет → Тур 1 | Тур 2…»); Shvager packages list the author's themes.

### 8. User Authentication

Registration with email confirmation (Mailjet), login with lockout, logout, password reset via email, and profile editing. Routes under `/Account/*` (Register, Login, Logout, Profile, ForgotPassword, ResetPassword, ConfirmEmail, RegisterConfirmation, ResendConfirmation). Transactional emails use the «База українських запитань» branding.

### 9. User Roles

| Role | Description |
|------|-------------|
| Anonymous | View published packages (AccessLevel All) |
| User | View published packages incl. RegisteredOnly, edit own profile |
| Editor | Create/edit/import own packages of both types |
| Admin | Manage all packages, users, editors, API keys |

### 10. Media Support

Images (.jpg, .jpeg, .png, .gif, .webp, .svg), videos (.mp4, .webm, .ogg), audio (.mp3, .wav, .ogg, .m4a) with lazy loading and caching. Available to both game types (handouts and comment attachments).

### 11. Package Management (CRUD)
**Routes**: `/manage/packages` (list), `/manage/package/{id}` (editor). Requires Editor or Admin role.

#### 11.1 Package List (Мої пакети)

Single table of the user's packages (Admins see all) with a **type badge column** and a type filter. Shows title, play date, tour/theme count, question count, status, owner (Admins). Creating a package requires choosing its type (ЩДК / Своя гра); the type cannot be changed later. The import section lives on this page (see §12).

#### 11.2 Package Editor (Редагування пакету)

Single-page editor; the header shows the package's type badge. Common to both types: title, description, play dates, preamble, status, publication date, access level, SharedEditors, tags, auto-save on blur.

**ЩДК mode** (unchanged behavior):
- NumberingMode select (Наскрізна/Потурова/Ручна) with automatic renumbering
- Tours accordion: add tour/warmup, per-tour type select (Regular/Warmup/Shootout, at most one warmup — always first, one shootout — always last), editors, preamble, blocks support (split into blocks, per-block editors/preamble, orphan questions), drag & drop reordering of tours and questions with renumbering
- Question editor modal: Number (editable only in Manual mode), HostInstructions, handout text/media, Text, Answer, Залік, Незалік, Comment + media, Source, authors; prev/next navigation; «Create next»

**Shvager mode**:
- No NumberingMode select, no warmup/shootout, no blocks
- Themes accordion: «Додати тему»; each theme has a **Title** input, authors (theme editors), preamble
- Questions within a theme get values automatically by position (10, 20, 30, 40, 50, …); drag & drop reordering reassigns values; themes reorder freely («Тема N» renumbers)
- **Pinned values survive renumbering.** Reserve and shoot-out questions — the substitutes printed after the last theme — carry a *range* value («10-30», «40-50») meaning they may replace any question in that band. Any value that isn't a plain integer is treated as chosen rather than positional, so `PackageRenumberingService` leaves it alone while the question still occupies its slot (`[10, 20, 10-30, 40, 50]` stays exactly that; neighbours keep the value their own position gives them). Such values can only arrive from the import — the modal's value field is read-only
- Question editor modal: value (read-only), Text, Answer, Залік, Незалік, **Форма**, Comment + media, Source, handout text/media, authors (no HostInstructions)
- **Soft validation on publish**: warnings (non-blocking) for themes without a title, themes with ≠5 questions. A **reserve theme is exempt from the count warning** — a theme holding at least one range-valued question holds however many substitutes the editors printed. Same rule at import time (`ShvagerValues.IsReserveTheme`); an empty theme is never a reserve theme, so it still warns

**Package Status**: Draft (owner/admin only) → Published (visible per access level) → Archived (hidden from lists, direct link only).

**Package Access Level**: Всі / Зареєстровані користувачі / Лише редактори. Admins and owners always have access.

### 12. Package Import

On `/manage/packages`, the import section offers **two clearly separated upload zones**:

- **Імпорт Що?Де?Коли?** — existing DOCX pipeline (tour-based formats)
- **Імпорт Своя гра** — Shvager DOCX pipeline (theme-based formats)

The chosen zone determines the parser — no content sniffing for DOCX. `.qhub` files are accepted in either zone; their embedded `gameType` decides the actual type (mismatch with the zone produces a warning).

Imports run as background jobs (queued, progress, retry with backoff, warnings, artifacts for debugging). Imported packages are created as **Draft**. See [PACKAGE_IMPORT.md](PACKAGE_IMPORT.md) and [IMPORT_DEBUGGING.md](IMPORT_DEBUGGING.md).

**Shvager DOCX formats supported** (both must import cleanly; samples in `_shvager/`):
- **Named-theme format** (2026-style): «Тема: {Назва} ({Автори})» headers (also «Тема.»), «Форма:» label, theme list in preamble
- **Bare-title format** (2021-style): theme headers are bare title lines; the «Теми:» list in the package header anchors theme detection; per-question «Автор: Ім'я Прізвище (Місто)» lines (city stripped); inline «[Малюнок N {url}]» handout references

Parsed labels: Відповідь, Залік, Незалік, Форма, Коментар, Джерело, Автор. Values must appear in 10→50 order within a theme; deviations produce warnings, not failures.

### 13. Package Export (.qhub)
**Route**: `GET /api/packages/{id}/export`

Any package can be exported as a `.qhub` archive (ZIP: `package.json` + assets). The format carries `gameType`, theme titles, and the Форма field. See [PACKAGE_FORMAT.md](PACKAGE_FORMAT.md).

### 14. Admin

- `/admin/editors` — editors list (read-only for Editors); promote/demote, author↔user linking
- `/admin/users` — all users, search, promote to Editor (auto-creates/links Author)
- `/admin/api-keys` — API key management for the public API

### 15. Public API (v1)

API-key authenticated, CORS-restricted, rate-limited. See [API.md](API.md).

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/packages?type=www\|shvager` | Package list; optional game-type filter; DTOs include `gameType` |
| GET | `/api/v1/packages/{id}` | Full package; tours carry `title` (themes), questions carry `form` |
| GET | `/api/v1/search?q=&type=www\|shvager` | Question search; results include `gameType` and `tourTitle` |
| GET | `/api/v1/editors` | Authors list |
| GET | `/api/v1/tags/popular` | Popular tags |

Internal (cookie-auth) endpoints: `/api/packages/search` (home filters, incl. `type`), `/api/packages/{id}/export`, `/api/media/*`, `/api/Auth/*`.

### 16. Notifications

Telegram notification on package publish includes the package's game type.

### 17. Database Seeding

On startup: creates roles (Admin, Editor, User), creates admin user from environment variables, seeds sample packages if database is empty.

### 18. Tournament Results & Question Statistics (ЩДК only)

See [RESULTS.md](RESULTS.md) for the full design; plan in [Stats_plan.md](Stats_plan.md).

- **Results sources** attached per package (manage page card «Результати»): Рейтинг МАК
  (rating.chgk.info, official API), OpenQuiz (open-quiz.com, static results.json), «Інше»
  (display-only external link). Loaded synchronously with spinner; reload keeps old data on
  failure; Rating embargo (hideResultsTo) surfaced with the date.
- **Per-question stats**: «Взяли: X/Y (Z%)» inside the answer spoiler (package page and
  question permalink), precomputed in the `QuestionStats` table, summed over all sources.
  Warm-up questions never have stats; shoot-out stats shown when a platform provides them
  (own denominator).
- **Results page** `/package/{id}/results`: aggregated standings across all platforms
  (re-ranked by points, ties share places, per-tour counts, «П» column, platform badges) +
  question-difficulty SVG chart with tour bands; bars link to question anchors.
- Package header: primary «Результати» link (internal, preferred) + secondary external
  platform links.

---

## UI/UX Features

- Full Ukrainian interface with Ukrainian date formatting (uk-UA)
- Responsive mobile-friendly design using Bootstrap; light/dark theme toggle
- **Type badges**: consistent `PackageTypeBadge` component (ЩДК / Своя гра) on cards, package pages, question cards, search results, and management tables
- **Icon System**: SVG sprite (`icons.svg`) with reusable `Icon.razor` component. See [ICONS.md](ICONS.md)
- Spoilered answers and 18+ blur are pure client-side JS (no server roundtrips)

---

## Project Structure

```
/QuestionsHub.Blazor/
├── Components/
│   ├── Account/           # Authentication pages
│   ├── Layout/            # MainLayout, NavMenu, TopSearchBar, ThemeToggle
│   ├── Pages/             # Home, PackageDetail, QuestionDetail, Search, Authors,
│   │   │                  # EditorProfile, Disclaimer, ManagePackages, ManagePackageDetail
│   │   └── Admin/         # Editors, Users, ApiKeys
│   ├── PackageTypeBadge.razor  # Game-type badge (ЩДК / Своя гра)
│   ├── QuestionCard.razor / QuestionContent.razor
│   ├── TourNavigation.razor    # Sidebar navigation (tours or themes)
│   ├── PackageImportSection.razor / ImportWarnings.razor
│   └── AuthorSelector.razor, TagSelector.razor, Icon.razor, MediaDisplay.razor
├── Controllers/           # AuthController, MediaController, PackageListController,
│   │                      # PackageExportController
│   └── Api/V1/            # PackagesController, SearchController, MetadataController
├── Data/                  # DbContext, seeding, migrations
├── Domain/                # Package (Type), Tour (Title), Block, Question (AnswerForm),
│                          # Author, Tag, PackageImportJob, enums
├── Infrastructure/
│   ├── Search/            # SearchService, SearchQueryParser, HighlightSanitizer
│   ├── Import/            # Job pipeline, DocxExtractor, PackageParser (ЩДК),
│   │                      # ShvagerParser (Своя гра), QhubExtractor, PackageDbImporter
│   ├── Export/            # QhubExporter
│   └── ...                # PackageListService, PackageManagementService,
│                          # PackageRenumberingService, AuthorService, TagService, MediaService
└── wwwroot/               # app.css, icons.svg, question-card.js, home-filters.js, ...
```

---

## Quick Reference: What Works Now

| Feature | ЩДК | Своя гра |
|---------|-----|----------|
| View packages list (typed tabs) | ✅ | ✅ |
| View package details | ✅ | ✅ |
| View questions with answers | ✅ | ✅ |
| Search with type filter | ✅ | ✅ |
| Create/edit packages | ✅ | ✅ |
| DOCX import | ✅ | ✅ (both format generations) |
| .qhub import/export | ✅ | ✅ (format 1.1, `gameType`) |
| Public API | ✅ (`type` filter, `gameType` fields) | ✅ |
| Author stats per type | ✅ | ✅ |
| Authentication, roles, media, tags, admin | ✅ shared | ✅ shared |
| Tournament results & question stats | ✅ (Rating, OpenQuiz, links) | ❌ (postponed) |
| Interactive play mode | ❌ | ❌ |
| Comments/ratings | ❌ | ❌ |

---

## Version History

| Date | Version | Changes |
|------|---------|---------|
| Jul 6, 2026 | 2.1 | **Tournament results & question statistics** (ЩДК): results sources per package (Рейтинг МАК API, OpenQuiz, external links), per-question «Взяли: X/Y» stats, `/package/{id}/results` page with aggregated standings + difficulty SVG chart, management card with sync load/reload |
| Jul 2, 2026 | 2.0 | **Dual game types**: Своя гра (Shvager) packages alongside Що?Де?Коли? — PackageType discriminator, themes (Tour.Title), question values, Форма field, typed home tabs, search type filter, split author stats, second importer, .qhub gameType, generic branding. Spec also caught up with shipped features: question permalinks, tags/18+, import pipeline, .qhub export, public API, admin API keys, dark theme |
| Jan 18, 2026 | 1.9 | «Create next» inherits block; orphan questions visible and draggable to blocks |
| Jan 17, 2026 | 1.8 | Icon system: centralized SVG sprite + Icon.razor |
| Jan 16, 2026 | 1.7 | Block entity: optional blocks within tours |
| Jan 10, 2026 | 1.6 | Mailjet email integration (confirmation, password reset) |
| Jan 7, 2026 | 1.5 | Public Authors page (/editors) |
| Jan 5, 2026 | 1.4 | Admin user management, Author-User linking |
| Jan 4, 2026 | 1.3 | Authors as separate entity, AuthorSelector, EditorProfile |
| Jan 2026 | 1.2 | Removed unused Package Management REST API |
| Jan 2026 | 1.1 | Preamble on Package/Tour, removed Tour Title, media upload |
| Dec 2025 | 1.0 | Initial specification document |
