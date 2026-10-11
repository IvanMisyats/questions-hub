# Favorites (Обране) — Implementation Plan

A logged-in user marks questions, tours (Своя гра themes), blocks and packages with a heart, then reviews
them on `/favorites`. Motivating use case: building a personal shortlist for an external "best of 2026"
vote (the voting itself is off-site). Branch: `feature/favorites`. This document is the source of truth:
design decisions, phase list and a live checklist. Update it at the end of every phase.

## Decision Log

| # | Decision | Choice |
|---|----------|--------|
| 1 | Who | Logged-in users with a **verified email** (`EmailConfirmed` claim). Anonymous visitors see an outline heart that leads to login with `returnUrl` |
| 2 | Visibility | **Private** per user. No public counts, no shared lists |
| 3 | What | Question, tour/theme, block, package. Favoriting a tour does **not** add its questions |
| 4 | Lists | One flat list. No collections, no notes, no export (v1) |
| 5 | Heart placement | Every question card (package page, `/question/{id}`, search results, editor profile), every tour/theme heading, every block header, the package title. Not the home list |
| 6 | Heart behavior | Instant optimistic toggle (outline ↔ filled red) with a tooltip; reverts on failure; no toast or confirmation |
| 7 | Page | `/favorites` ("Обране" in the sidebar for logged-in users): tabs Запитання / Тури / Блоки / Пакети with counts; filters by game type and **played year** (`PlayedFrom`..`PlayedTo`, falling back to `PublicationDate`); newest first; paginated. Questions are full-breadcrumb `QuestionCard`s with the answer hidden; unfavoriting keeps the card until reload (undo) |
| 8 | Deleted item | Favorite is deleted with it (DB cascade). Re-uploading a package creates new IDs, so its old favorites go — accepted |
| 9 | Inaccessible item | Favorite kept but hidden (and not counted); it comes back if access returns. 18+ stays blurred |
| 10 | Cap | 5,000 favorites per user (count check, no lock; a small concurrent overshoot is harmless) |
| 11 | Out of scope (v1) | Agent API / MCP access to favorites (backlog), export, collections, notes, public counts |

### Data model: options considered

- **A (chosen)** — one `Favorites` table. `PackageId` always set (the package containing the item);
  `QuestionId` / `TourId` / `BlockId` nullable, **at most one** set; none set = package favorite. All FKs
  cascade. One query fetches every heart on a package page; access, type and year filters join `Package`
  directly; one service, one endpoint, one page query.
- **B** — four tables, composite PKs. No constraints, but four query paths, four join chains for the
  filters, four count queries.
- **C** — polymorphic `(EntityType, EntityId)` without FKs. **Rejected**: package and tour deletes rely on
  Postgres cascades the app never sees (`PackageService.DeletePackage` doesn't load blocks/stats), so
  orphan rows would accumulate.

The denormalized `PackageId` is safe because questions, tours and blocks never move between packages:
changeset and editor moves stay inside one package.

## Data Model — `Favorite` (table `Favorites`)

| Column | Type | Notes |
|---|---|---|
| `Id` | int PK | |
| `UserId` | string, required | FK → `AspNetUsers`, cascade |
| `PackageId` | int, required | FK → `Packages`, cascade |
| `QuestionId` | int? | FK → `Questions`, cascade |
| `TourId` | int? | FK → `Tours`, cascade |
| `BlockId` | int? | FK → `Blocks`, cascade |
| `CreatedAt` | timestamptz | UTC |

`Kind` (`FavoriteKind`: Question / Tour / Block / Package) is derived from which nullable FK is set.

Constraints and indexes — see **Performance** for why each index leads with the item column:

- `CK_Favorites_SingleTarget`: `num_nonnulls("QuestionId","TourId","BlockId") <= 1`
- `IX_Favorites_QuestionId_UserId` unique (NULLs are distinct, so no filter needed)
- `IX_Favorites_TourId_UserId` unique
- `IX_Favorites_BlockId_UserId` unique
- `IX_Favorites_PackageId_UserId` (non-unique) — per-package lookup + package-delete cascade
- `IX_Favorites_PackageId_UserId_PackageOnly` unique, partial: `WHERE "QuestionId" IS NULL AND "TourId" IS NULL AND "BlockId" IS NULL`
- `IX_Favorites_UserId_CreatedAt` (`CreatedAt DESC`) — the favorites page, and the user-delete cascade

## Performance

The feature must not slow existing pages down.

- **Anonymous visitors: zero extra queries.** The lookup is skipped when there is no user id.
- **Logged-in pages: one extra indexed query each.**
  - Package page: `WHERE "PackageId" = @p AND "UserId" = @u` → `IX_Favorites_PackageId_UserId`; returns every heart on the page (questions, tours, blocks, the package).
  - Search, question page, editor-profile "load more": `WHERE "UserId" = @u AND "QuestionId" = ANY(@ids)` → `IX_Favorites_QuestionId_UserId`.
  - Selects only the id columns; no tracking.
- **Cascade deletes stay index-driven.** Postgres does **not** index FK columns automatically. Deleting a
  package with 1,000 questions fires the `Questions → Favorites` cascade 1,000 times; without an index
  leading with `QuestionId`, each would scan the whole table. Every FK column (`QuestionId`, `TourId`,
  `BlockId`, `PackageId`, `UserId`) is the **leading** column of some index.
- **Toggle endpoint.** Resolves the target with one projection query (item → `PackageId` + the package's
  access fields), never loads the graph. Existence check + insert hit the unique indexes.
- **Favorites page.** Paged via `(UserId, CreatedAt DESC)`; tab counts in one `GROUP BY` query; the year
  list from a small projection (≤ 5,000 rows); question cards load the same includes as `EditorProfile`
  for one page only.
- **No changes** to the home page, the package list API, search SQL, or any existing table's schema.
  The migration only creates the new table.
- **JS.** `favorites.js` does nothing at page load; it only acts on click.

## Authentication & CSRF

- Endpoint policy `FavoritePolicies.SignedInBrowser`: `IdentityConstants.ApplicationScheme` (cookie) only,
  authenticated, `RequireClaim(CustomUserClaimsPrincipalFactory.EmailConfirmedClaim, "True")` (the claim value is
  capitalized — `bool.ToString()`). A PAT or API key is never accepted.
- `[ValidateAntiForgeryToken]`. JS reads `input[name=__RequestVerificationToken]` (every logged-in page has
  one: the logout form in `LoginDisplay.razor`) and sends it as the `RequestVerificationToken` header.
- Per-user rate-limit budget `user.favorites` (60/min, keyed on `NameIdentifier`).
- Anonymous call → 401 (verified by an integration test; if the cookie handler redirects instead, add
  an `OnRedirectToLogin` override for `/api/favorites`).

## API — `FavoritesController`

- `PUT /api/favorites/{kind}/{id}` — add (idempotent) → `{ "isFavorite": true }`
- `DELETE /api/favorites/{kind}/{id}` — remove (idempotent) → `{ "isFavorite": false }`
- `kind` ∈ `question` | `tour` | `block` | `package`
- 404 — target missing or not viewable by the user; 409 — cap reached (Ukrainian message)

## UI

- Icons `heart` / `heart-fill` (Bootstrap Icons) in `wwwroot/icons.svg`.
- `FavoriteButton.razor` (`Kind`, `Id`, `FavoriteLookup`). Renders nothing when the page passes no lookup;
  a login heart for `FavoriteLookup.Anonymous`; otherwise a toggle. Styled like `.copy-link-btn`.
- `wwwroot/favorites.js`: optimistic toggle, button disabled while in flight, revert + `title` message on
  error, 401 → login redirect, keeps every button with the same `kind:id` in sync.
- `QuestionCard` gets an optional `FavoriteLookup? Favorites` parameter.
- `/favorites` (`Favorites.razor`): static SSR, `[StreamRendering]`, `[Authorize]`; query `tab`, `type`,
  `year`; route-segment paging like `Authors.razor`.
- Shared helpers so the favorites page and the package page can't drift: tour display name
  (Розминка / Перестрілка / Тур N / `ThemeDisplayName`) and package-wide block numbering.

## Phased Plan

- [x] **Phase 0** — plan doc, CLAUDE.md row
- [x] **Phase 1** — data + service: entity, DbContext config, migration, `PackageAccessContext.GetViewFilter()`, `FavoriteService`, `FavoriteLookup`; unit tests; Postgres integration tests (cascades through the real delete paths, unique indexes, check constraint)
- [x] **Phase 2** — endpoint: controller, policy, budget; HTTP integration tests (add/remove/idempotent, anonymous 401, PAT rejected, antiforgery required, hidden draft 404, cap 409)
- [x] **Phase 3** — hearts: icons, `FavoriteButton`, `favorites.js`, wiring into PackageDetail / QuestionCard / QuestionDetail / Search / EditorProfile; UI integration test (the naming helpers moved to Phase 4, where they are used)
- [x] **Phase 4** — `/favorites` page + nav; UI integration tests (tabs, counts, filters, hidden items, 18+ blur)
- [x] **Phase 5** — docs (`SITE_SPECIFICATION.md`, `BACKLOG.md`), full test run, end-to-end check in the real app

## Risks & Notes

- The antiforgery token on the page comes from `LoginDisplay`'s logout form. If that ever changes,
  hearts would fail — an integration test asserts the token is present, and the JS logs a clear error.
- `EditorProfile` is InteractiveServer: Blazor diffs its render tree, not the DOM, so a JS-toggled heart
  keeps its state across "load more" re-renders.
- InMemory unit tests can't check constraints or cascades — those live in the Postgres integration tests
  (they skip locally without Docker, never on CI).

## Phase Notes

### Phase 0 — Plan (2026-10-11)
Grill + plan approved by Ivan. Ivan's extra requirement: performance must not degrade (see Performance).

### Phase 1 — Data + service (2026-10-11)
- `Favorite` entity + `FavoriteKind`, migration `AddFavorites` (creates only the new table), `PackageAccessContext.GetViewFilter()`,
  `Infrastructure/Favorites/FavoriteService` + `FavoriteLookup`, registered in `AddDatabase`.
- `Add` = 4 short queries (resolve viewable package, duplicate probe, cap count, insert). The duplicate probe stays on
  purpose: without it every repeated click would raise a unique violation that EF logs at Error level.
- The Question-tab page query uses `AsSplitQuery()` — a single joined query would repeat each question's text per
  author × tag. Page number is clamped (`MaxPage`) so a hand-typed huge page can't overflow the offset.
- `PackageService.DeletePackage` needs a Razor auth scope, so the Postgres cascade test reproduces its exact EF delete
  (tours + questions tracked, the rest left to DB cascades).
- Tests: `FavoriteServiceTests`, `PackageViewFilterTests` (parity with `CanViewPackage` over every status × access level ×
  owner × viewer), `FavoritesPostgresTests` (cascades on package/tour/question/block delete, every unique index and the
  check constraint by name, concurrent add, SQL translation of the page and lookup queries).

### Phase 2 — Endpoint (2026-10-11)
- `FavoritesController` (`PUT`/`DELETE /api/favorites/{kind}/{id}`), policy `FavoritePolicies.SignedInBrowser`,
  `[ValidateAntiForgeryToken]`, per-user budget `user.favorites` (`RateLimits:UserFavoritesPerMinute`, default 60).
  The claim name is now one constant, `CustomUserClaimsPrincipalFactory.EmailConfirmedClaim`.
- Confirmed: .NET 10 answers an anonymous call to an `[ApiController]` endpoint with 401, not a login redirect.
- Not done, on purpose (from the review): no per-IP policy — the 401 path is cheap (no DB) and the site's pages
  aren't IP-limited either; the budget is charged before the antiforgery filter, but cross-site requests carry no
  cookie (SameSite=Lax), so they never reach a user's partition. The `EmailConfirmed` claim is a cookie snapshot;
  nothing in the app un-confirms an email today.
- Tests: `FavoritesHttpTests` — toggle + idempotency, every kind, anonymous 401, PAT alone 401, API key alone 401,
  missing antiforgery 400, hidden draft / missing item / unknown kind 404, cap 409, budget 429.

### Phase 3 — Hearts (2026-10-11)
- `FavoriteButton.razor` (+ scoped CSS), `wwwroot/favorites.js`, `heart` / `heart-fill` icons. Pages get their lookup
  from `FavoriteService.GetLookupForPackage/ForQuestions(PackageAccessContext, …)`: `Anonymous` for visitors (no
  query), null (no hearts) without a verified email, otherwise one indexed query.
- `FavoriteKindSegments` is the one kind ↔ URL-segment mapping (button and controller).
- Errors show in a small toast (`.fav-toast` in `app.css`): tooltips don't appear on touch screens. 401 → login
  page; 403 / 429 / other errors → revert + Ukrainian message.
- `EditorProfile` is InteractiveServer: JS toggles don't reach the circuit, so the page re-reads the hearts of its
  loaded questions (one query) whenever the list is shown or grows; otherwise re-created cards showed stale state.
  This replaced `FavoriteLookup.Merge`.
- On the search page the heart lookup has its own try/catch: if it fails, the results still render, without hearts.
- Deviation: the tour-name / block-numbering helpers moved to Phase 4, where the favorites page needs them.
- Known, accepted: hearts are shown from the DB email flag, the endpoint checks the cookie claim. They can only
  disagree for a signed-in unverified user, and unverified users cannot sign in (`RequireConfirmedEmail`).

### Phase 4 — Favorites page (2026-10-11)
- `Components/Pages/Favorites.razor` (`/favorites`, `/favorites/{page}`; query `tab=questions|tours|blocks|packages`,
  `type=www|shvager`, `year`), «Обране» in `NavMenu` for signed-in users.
- Shared naming: `PackageLabels.TourName` / `PackageLabels.BlockNumbers` and `TourOrdering.ForDisplay`, now used by
  PackageDetail, TourNavigation, QuestionCard (warm-up/shootout questions now read «Розминка → …» instead of
  «Тур 0 → …», also on the editor profile) and the favorites page.
- Unnamed blocks («Блок N») are numbered across their package by `FavoriteService` (one extra narrow query on the
  block tab, none when every block on the page has a name).
- `year` is bound as a string so a hand-edited value can't fail the page; a page past the end says so.
- Not done, on purpose (review): passing block numbers from PackageDetail into TourNavigation (computing them twice
  costs microseconds and uses the same helper); hiding the nav link from unverified users (they cannot sign in).

### Phase 5 — Docs + verification (2026-10-11)
- `SITE_SPECIFICATION.md` (§19 Favorites, internal endpoints, structure, version 2.2), `BACKLOG.md` (favorites
  follow-ups: agent API read access, export, collections/notes).
- Full suite green: 1394 unit, 95 integration (Postgres via Testcontainers). No new build warnings.
- End-to-end against the dev app and dev DB (156 packages, 6168 questions), over HTTP with curl — Chrome
  automation is blocked for `localhost` by organization policy, so clicking in a real browser (the JS toggle,
  the toast) was not exercised; `favorites.js` passed `node --check`. Verified: visitor hearts + 401 + login
  redirect for `/favorites`; register → hearts on all kinds; PUT/DELETE idempotent; missing antiforgery → 400;
  filled hearts on the package and question pages; `/favorites` tabs, counts, type/year filters, `?year=abc`,
  a page past the end, the nav link; removal updates the counts. Test users deleted afterwards.
- Query plans with ~63k favorites (every question × every user, in a rolled-back transaction): package-page
  lookup 0.06 ms (`IX_Favorites_PackageId_UserId`), 50-question lookup 0.23 ms (index-only on
  `IX_Favorites_QuestionId_UserId`), tab counts 0.6 ms, first page 0.07 ms (`IX_Favorites_UserId_CreatedAt`).
  Deleting a package with 37 questions: 15.6 ms total, of which the favorites cascades ~3 ms, all index-driven.

## Deployment
- Nothing manual: the `AddFavorites` migration is applied on startup and only creates the new table. nginx routes
  `/api/favorites` through the catch-all location; Cloudflare has no method restrictions.
- Optional setting: `RateLimits:UserFavoritesPerMinute` (default 60).
