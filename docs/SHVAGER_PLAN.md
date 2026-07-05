# Своя гра (Shvager) — Implementation Plan

Target state is described in [SITE_SPECIFICATION.md](SITE_SPECIFICATION.md) (v2.0); game rules and source-format reference in [shvager.md](shvager.md). This document records the design decisions, the technical approach per subsystem, and the phased delivery plan.

## Decision Log

| # | Decision | Choice |
|---|----------|--------|
| 1 | Data model | **Reuse existing entities** + `PackageType` discriminator on `Package`; no parallel table set |
| 2 | URLs | Shared routes (`/package/{id}`, `/question/{id}`, `/search`) + prominent type badge in UI |
| 3 | Main page | Type tabs «Що?Де?Коли?» (default) / «Своя гра», persisted in query string |
| 4 | UI type label / branding | «Своя гра»; site rebranded to generic «База українських запитань» (nav brand «База запитань») |
| 5 | Theme titles in search | Display-only in result cards (via Tours join); **not** FTS-indexed in v1 |
| 6 | Manage & import | One package list with type badge/filter; two explicit upload zones select the parser |
| 7 | Theme structure | Auto-values by position ((OrderIndex+1)×10); soft (non-blocking) publish warnings for ≠5 questions or missing title |
| 8 | Author stats | Fully split per type on `/editors` and `/editor/{id}` |
| 9 | Importer coverage | Both DOCX generations: named-theme (2026, «Тема:») and bare-title (2021) |
| 10 | Question value storage | In existing `Question.Number` display string; `OrderIndex` stays the ordering source of truth |

### Rejected alternatives (and why)

- **Separate `ShvagerPackage`/`Theme`/`ShvagerQuestion` tables** — would duplicate FTS columns/indexes, access control, author links, media handling, import job plumbing, and most of the management UI for ~90% identical shapes. The differences (theme title, value, form) are three nullable columns.
- **Dedicated `Value` int column** — `Question.Number` is already a display string end-to-end (search, DTOs, UI); ordering never relies on it. A separate int adds a migration and a second source of truth with no consumer today. Revisit if/when a play mode needs arithmetic on values.
- **FTS-indexed theme titles** — Postgres generated columns cannot reference other tables, so indexing `Tour.Title` into `Questions.SearchVector` requires an app-maintained denormalized column + backfill + write-path hooks. Deferred; display-only is enough for v1.
- **Content sniffing for DOCX imports** — explicit user choice is more predictable and matches the requirement of “clearly two different importers”.

---

## Data Model Changes

### New enum

```csharp
public enum PackageType
{
    /// <summary>Що?Де?Коли? (team game, tours of numbered questions).</summary>
    Www = 0,
    /// <summary>Своя гра / Швагер (buzzer game, themes of 5 valued questions).</summary>
    Shvager = 1
}
```

`Www = 0` so existing rows need no data migration.

### Columns (one EF migration, `AddPackageType`)

| Entity | New member | Notes |
|---|---|---|
| `Package` | `PackageType Type` (default `Www`) | Immutable after creation (no UI to change) |
| `Tour` | `string? Title` (max ~200) | Theme name; null for ЩДК tours |
| `Question` | `string? AnswerForm` | «Форма»; not added to FTS generated columns in v1 |
| `PackageImportJob` | `PackageType Type` (default `Www`) | Set by the upload zone; drives parser dispatch |

No index needed on `Packages.Type` initially (small table, always combined with Status filter); add later if profiling says so.

### Semantics per type

- **Shvager**: `NumberingMode` ignored (stays at default), `TourType` always `Regular`, `Blocks` never created, `HostInstructions` unused. Enforced by UI/import, not by DB constraints.
- `Question.Number` for Shvager = `((OrderIndex + 1) * 10).ToString()` — maintained by `PackageRenumberingService`.

---

## Subsystem Designs

### 1. Renumbering (`Infrastructure/PackageRenumberingService.cs`)

Branch at the top on `package.Type`:
- `Shvager`: tour numbers `1..N` by OrderIndex (no warmup/shootout repositioning); question numbers `(index+1)*10` within each tour; NumberingMode ignored.
- `Www`: existing logic untouched.

Unit tests for the new branch (reorder within theme, reorder themes, 6+ questions in a theme → values 10..60 with no failure — validation is the editor’s job, not the renumberer’s).

### 2. Viewing pages

- **`PackageTypeBadge.razor`** (new): small badge, `bg-primary` for ЩДК, `bg-success` for «Своя гра»; used on Home cards, PackageDetail header, QuestionDetail, Search results, ManagePackages rows.
- **`PackageDetail.razor` + `TourNavigation.razor`**: per-type heading logic — Shvager: «Тема {n}. {Title}» (fall back to «Тема {n}» when untitled); the narrow sidebar shows the title alone; no warmup/shootout/block branches; tour sort for Shvager is plain OrderIndex (skip the parse-Number-as-int WWW ordering).
- **`QuestionCard.razor`**: Shvager header shows the value badge instead of «Запитання N».
- **`QuestionContent.razor`**: render `AnswerForm` inside the answer section, after Незалік, labeled «Форма:» (matches source-document ordering).
- **`QuestionDetail.razor`**: per-type breadcrumb.

The page needs `Package.Type` (already loaded — the whole aggregate is fetched) and passes it down via component parameters.

### 3. Home / browse (`Home.razor`, `home-filters.js`, `PackageListController`, `PackageListService`, `PackageListFilter`)

- `PackageListFilter` gets `PackageType? Type` (nullable — the internal API accepts `type` and returns both types when omitted; the home page always passes its active tab, and home-filters.js always sends `type`). Undefined enum values are coerced to null in the controller to keep the per-type caches bounded.
- `PackageCardDto` gets `PackageType Type` and `int TourCount` (for «N тем · M запитань» on Shvager cards).
- `Home.razor`: tab strip above filters; active tab from `?type=`; counts, tag pills, and editor dropdown all scoped to the active type (editor dropdown and popular tags should be filtered by type to avoid dead filters).
- `home-filters.js`: carry `type` through its query-string state.

### 4. Search (`Infrastructure/Search/SearchService.cs`, `Search.razor`, `Api/V1/SearchController.cs`)

- `SearchService.Search` gains `PackageType? typeFilter`; SQL adds `t."Title" AS "TourTitle"`, `p."Type" AS "PackageType"` to the SELECT and `AND (@type IS NULL OR p."Type" = @type)` to WHERE. `SearchResult` record extended accordingly.
- `Search.razor`: filter chips «Усі / Що?Де?Коли? / Своя гра» (`?type=` query param, default all); result card context line per type; show «Форма» in Shvager cards; type badge per result.
- API: `GET /api/v1/search?type=www|shvager`; `ApiSearchResultDto` gains `gameType` and `tourTitle`. Invalid `type` → 400.
- No FTS DDL changes — the generated columns don’t reference package data.

### 5. Authoring (`ManagePackages.razor`, `ManagePackageDetail.razor`, `PackageManagementService`)

- **Create package**: type choice (two buttons or a select in the create modal). Type is stored once; no edit UI.
- **`ManagePackages.razor`**: type badge column + type filter on the table.
- **`ManagePackageDetail.razor`** (the 3175-line monolith): gate WWW-only UI on `package.Type == PackageType.Www` — NumberingMode select, SharedEditors stays for both, tour-type select, «Додати розминку», block controls. Shvager additions: theme Title input in the accordion header area, «Форма» field in the question modal, hide HostInstructions and Number editability, value badge on question rows. Terminology switches («Тур» → «Тема», «Додати тур» → «Додати тему») via small helper methods rather than duplicated markup.
- **`PackageManagementService`**: creation paths set `Tour.Title` for Shvager; block/tour-type operations guarded against Shvager packages (defense in depth); all mutations keep calling the renumbering service, which now handles both types.
- **Soft publish validation**: when switching a Shvager package to Published, collect warnings (theme without title, theme with ≠5 questions) and show a confirm dialog; never block.

### 6. Import

**Pipeline reuse**: job entity/queue/background service/retry/artifacts, `DocxExtractor` → `List<DocBlock>`, asset persistence, and `PackageDbImporter` all stay. Changes:

- **`PackageImportSection.razor`**: two upload zones («Імпорт Що?Де?Коли?» / «Імпорт Своя гра»); `PackageImportService.Enqueue` gains a `PackageType` argument stored on the job.
- **Dispatch** in `PackageImportService.Process`: `.qhub` → `QhubExtractor` (gameType from `package.json`; warn if it contradicts the upload zone); `.docx` → `PackageParser` or `ShvagerParser` by `job.Type`.
- **`ShvagerParser`** (new sibling in `Infrastructure/Import/`, consuming `List<DocBlock>`, emitting `ParseResult`):
  - *Header*: first non-empty line(s) → package title (+ subtitle line appended); optional «Редактор {Ім'я Прізвище}» → package editors (SharedEditors=true); free text → preamble; optional «Теми:» list captured both into the preamble and as **theme-title anchors**.
  - *Theme start detection*, in priority order: (a) «Тема[:.] {Title} ({Authors})» line; (b) a line exactly matching one of the «Теми:» anchors; (c) fallback — a short non-label line immediately followed by a «10.» question start. Theme authors from parens; bare-title themes get authors from per-question «Автор:» lines.
  - *Questions*: `^\s*(10|20|30|40|50)\s*\.` starts a question; tolerate any value order but warn when not strictly ascending 10→50 or when count ≠ 5.
  - *Labels* (reuse the `ParserSection` routing approach): Відповідь, Залік, Незалік, **Форма** (new section), Коментар, Джерело, Автор (city-in-parens already stripped by `ParseAuthorName`).
  - *Assets*: embedded DOCX images via the existing association logic (pre-answer → handout, post-answer → comment). Inline «[Малюнок N {url}]» in question text: extracted into `HandoutText` as a link, removed from `Text`, with a warning listing the URL (external links are not downloaded).
  - New regexes live in `ParserPatterns.cs` (or a `ShvagerPatterns.cs` sibling if cleaner).
- **`PackageDbImporter`**: map `Package.Type`, `Tour.Title`, `Question.AnswerForm`; skip NumberingMode inference for Shvager.
- **DTOs**: `TourDto` gains `Title`, `QuestionDto` gains `Form`, `ParseResult` gains `PackageType`.

**Tests**: `ShvagerParserTests` (synthetic `Block(...)` cases per rule), golden tests over both `_shvager/` samples (import must succeed with expected theme/question counts and zero errors), `PackageDbImporter` mapping test, `.qhub` gameType round-trip test.

### 7. `.qhub` format v1.1 (`QhubModels`, `QhubExtractor`, `QhubExporter`, `docs/package-format.schema.json`)

- Package: `gameType: "www" | "shvager"` (optional, default `www` — v1.0 files stay valid).
- Tour: optional `title`. Question: optional `form`.
- Export writes the new fields; import maps them; schema + [PACKAGE_FORMAT.md](PACKAGE_FORMAT.md) updated.

### 8. Public API (`Api/V1`, `docs/API.md`)

- `GET /api/v1/packages?type=` filter; `ApiPackageDto`/`ApiPackageDetailDto` gain `gameType`; tour DTO gains `title`; question DTO gains `form`.
- Search param/DTO per §4. Additive-only — no breaking changes for existing consumers.

### 9. Author statistics (`AuthorService`, `Authors.razor`, `EditorProfile.razor`)

- Stats queries group by `Package.Type`: `/editors` columns — Автор | ЩДК: пакети | ЩДК: запитання | Своя гра: пакети | Своя гра: запитання; ranked by total questions. On narrow screens collapse to two combined columns with per-type detail in the cell.
- Profile: packages grouped into per-type sections; Shvager section links «Пакет → Тема «Назва»» for per-theme editors.

### 10. Branding

Replace «Що?Де?Коли?» branding with generic naming: `MainLayout.razor` top bar → «База українських запитань»; `NavMenu.razor` brand → «База запитань» (icon stays); `Home.razor` PageTitle; `MailjetEmailSender.cs` subjects/footers; `App.razor` title already generic. Telegram publish notification gains the type label.

---

## Phased Plan

Each phase is independently shippable (Shvager packages simply don’t exist until Phases 3–4 create them). Run `dotnet test` per phase; new services/branches get unit tests.

### Phase 1 — Foundation: schema, domain, renumbering
- `PackageType` enum; `Package.Type`, `Tour.Title`, `Question.AnswerForm`, `PackageImportJob.Type`; EF migration
- `PackageRenumberingService` Shvager branch + tests
- `PackageTypeBadge.razor`
- Guards in `PackageManagementService` (no blocks/warmup/shootout for Shvager) + tests

### Phase 2 — Viewing
- `PackageDetail` + `TourNavigation`: theme rendering, per-type ordering
- `QuestionCard`/`QuestionContent`: value badge, «Форма» in answer section
- `QuestionDetail` per-type breadcrumb; type badges on all of the above
- Verify with a hand-seeded Shvager package (SQL or temporary seeding)

### Phase 3 — Authoring
- Create-package type choice; `ManagePackages` badge column + filter
- `ManagePackageDetail` Shvager mode: themes accordion (Title input), question modal («Форма», read-only value, no HostInstructions), hidden WWW-only controls, terminology switch
- Soft publish warnings (title, ≠5 questions)
- `PackageManagementService` updates + tests

### Phase 4 — Import
- Two upload zones; `Enqueue`/job/dispatch by type
- `ShvagerParser`: both formats, labels incl. Форма, «Теми:» anchoring, bracketed picture links, warnings
- `PackageDbImporter` mapping (Type/Title/Form)
- Tests: parser unit tests, golden tests on both `_shvager/` samples, importer mapping tests
- Update [PACKAGE_IMPORT.md](PACKAGE_IMPORT.md) + [IMPORT_DEBUGGING.md](IMPORT_DEBUGGING.md)

### Phase 5 — Home page
- Type tabs (`?type=`), per-type counts, filtered tag pills / editor dropdown
- `PackageListFilter`/`PackageListService`/`PackageListController`/`home-filters.js` + card variants (`N тем · M запитань`)

### Phase 6 — Search
- `SearchService`: type filter + `TourTitle`/`PackageType` in results + tests
- `Search.razor`: filter chips, per-type result cards (theme context, Форма)
- `GET /api/v1/search?type=` + DTO fields; update [SEARCH.md](SEARCH.md) (also fix the stale `SearchResult` shape documented there)

### Phase 7 — Interchange, API, stats, branding
- `.qhub` v1.1 (gameType/title/form): exporter, extractor, JSON schema, [PACKAGE_FORMAT.md](PACKAGE_FORMAT.md)
- `GET /api/v1/packages?type=` + `gameType`/`title`/`form` in DTOs; [API.md](API.md)
- Split author stats (`/editors`, `/editor/{id}`)
- Branding sweep (layout, nav, emails, Telegram)
- Final pass over SITE_SPECIFICATION.md «Quick Reference» table (flip 🚧 → ✅)

**Suggested order rationale**: 1→2 makes the type visible end-to-end; 3→4 produce real Shvager data (the `_shvager/` samples validate the whole chain); 5→6 expose the data where users look for it; 7 is polish and ecosystem.

---

## Risks & Notes

- **`ManagePackageDetail.razor` is a 3175-line monolith** — the Shvager mode touches it throughout. Budget extra review/testing time in Phase 3; resist a full refactor mid-feature (extract small per-type helpers only).
- **Bare-title theme detection** (2021 format) is heuristic. The «Теми:» anchor list makes it reliable for the provided samples; keep the fallback (title line + «10.» lookahead) conservative and prefer a warning + manual fix over misparsing. The import artifacts (`working/extracted.json`, replay via unit test) are the debugging loop.
- **Doc drift**: `PACKAGE_IMPORT.md` describes an unimplemented LLM-normalization step, and `SEARCH.md` documents an outdated `SearchResult`; both get corrected in their phases.
- **Deferred**: FTS-indexed theme titles/Форма (needs denormalization), play mode (int values would then earn a real column), remembering the home-tab choice client-side.
