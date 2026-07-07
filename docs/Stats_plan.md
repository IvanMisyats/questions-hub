# Statistics & Tournament Results — Feature Plan

Question statistics + tournament results for Що?Де?Коли? packages: per-question correct-answer counts, links to tournament results (internal, loaded into our DB, preferred over external), an aggregated results page with team standings and a question-difficulty chart, and a management UI to attach/load/reload result sources from external platforms.

This document is the source of truth for the feature: the original request, the design decisions, the subsystem designs, and the live phased checklist.

---

## Feature request (original)

### WhatWhereWhen game type

Each package is played by teams. Quite often we know the results and the stats: how many and which teams answered each question, results of each team (number of correct answers, place, etc.).

1. For each question — display the number of correct answers and the total number of teams, e.g. **5/10** (5 correct answers out of 10 teams), in a collapsible part of the question section, after the author field.
2. Add links to tournament-results pages in the package section. There can be several, on different platforms. Internal results (loaded into the local DB) must be clearly distinguished from external ones (direct platform links); internal results are the preferred path — keep the user on our site.
3. Add a button on the package management page to attach a results link.
4. Load results/stats from external sites and persist them in the database. Reloading later must be possible (more teams played, results updated). Validate results/stats — they must not be empty. Notify on errors. If reloading fails — keep the old results. Load external results only once per explicit request (API limits).
5. The package view page must stay fast and light — cache or precalculate results/stats.
6. A new page or section with the aggregated results of the tournament (all teams from all platforms).
7. A section with aggregated per-question statistics: a chart with clear separation of tours (reference: a bar chart of correct-answer counts per question with alternating tour background bands), to analyze the difficulty of questions and tours.
8. Keep team names (when known), but not team squads (players).
9. In the future we may show detailed per-question results (list of teams that answered correctly) — load and store this data now.

Edge cases:
1. No statistics for warm-up and shoot-out questions (but see Decision 3 — shoot-out stats are shown when a platform provides them).
2. Quite often there are no results for a tournament at all.
3. Sometimes only per-question statistics are known, without tournament standings.
4. A package may be played on 2–3 different platforms, or several times on one platform (different dates/teams/languages). Stats must be summed over all plays.
5. Sometimes there are standings but no per-question statistics.

Platforms:
1. **Rating** — <https://rating.chgk.info> (tournament page `/tournament/{id}`, results `/tournament/{id}/statistics`; API <https://api.rating.chgk.info/>).
2. **OpenQuiz** — <https://www.open-quiz.com> (GitHub: <https://github.com/usix79/openquiz>).
3. **Other** — a plain link to an external site (Google Docs etc.); never loaded, just opened in a new tab.

### Shvager game type

Usually no structured results exist (sometimes ad-hoc Google Sheets). **Postponed.**

---

## Platform research (verified live, 2026-07-06)

### Rating (rating.chgk.info)

- Official API (API Platform/Symfony, OpenAPI 3.1 at `https://api.rating.chgk.info/docs.jsonopenapi`). **All reads are anonymous** — no credentials needed. A JWT (via `POST /authentication_token`) does **not** bypass the results embargo, so auth is useless for v1 and is not implemented.
- `GET /tournaments/{id}` → dates, editors, `type` (2 очник / 3 синхрон / 6 строгий синхрон / 8 асинхрон), and `questionQty` (per-tour question counts, e.g. `{"1":12,"2":12,"3":12}`).
- `GET /tournaments/{id}/results?includeMasksAndControversials=1` (with `Accept: application/json`) → per team: `team.id`, `team.name` (name at tournament time; `current.name` is today's name), `town`, `position` (float; ties share averaged position), `questionsTotal` (points), `mask` — one char per question in play order: `0` wrong, `1` correct, `?` pending controversial, `X` question removed; `null` when no mask exists.
- **Embargo**: until `hideResultsTo` passes, `mask`/`position`/`questionsTotal` are `null` (team list is visible earlier). Loader must surface this as a "results not available until {date}" error.
- Empty results (`[]`) for registered-but-unplayed tournaments. No observed rate limits; be polite anyway.
- Reference tournament 13097: 17 teams, 36 questions; per-question correct counts verified derivable from masks (Q11=14/17, Q6=0/17).

### OpenQuiz (open-quiz.com)

- No official API; results are a **static JSON file**: `GET /static/{quizId}-{resultsToken}/results.json` (the `token` query param of the public `results.html` link IS the results token; no other auth). Republished server-side on every admin action; append `?nocache={ticks}` to skip CDN cache.
- Shape: `Questions[]` — `{Key: {TourIdx, QwIdx}, Name, EOT, Score, ...}` in play order (`Name` is a display string: `"0"` warm-up, `"1".."60"` regular, `"101"+` shoot-out, `"8.1"` multi-question slips; `EOT: true` marks tour boundaries). `Teams[]` — `{TeamId, TeamName, Points (decimal), PlaceFrom, PlaceTo, Tours: [{Points}], Details}`.
- `Details` is a map keyed by **stringified compact JSON** `{"TourIdx":0,"QwIdx":0}` → `{Result, Vote}`: `Result > 0` correct, `null` wrong, **key absent** = no answer submitted. `Questions[].Score` is a difficulty score, *not* the correct count — always compute counts from `Details`.
- An «aud» link (`app/index.html?who=aud&quiz={id}&token={listenToken}`) can be exchanged for the results token: `POST /api/ISecurityApi/login` body `[{"Token":"","Arg":{"AudUser":{"QuizId":N,"Token":"..."}}}]` → JWT → `POST /api/IAudApi/getQuiz` body `[{"Token":"<JWT>","Arg":null}]` → response contains `RT` (results token). Works for finished quizzes.
- Reference quizzes: 25795 (44 teams, 65 questions incl. shoot-out "101".."105", 6 tours), 26130 (1 team, warm-up "0").
- Risk: unofficial — the URL scheme may change on a redeploy. Mitigated by persisting raw payloads and isolating the loader in one class.

---

## Decision log

| # | Decision | Choice |
|---|----------|--------|
| 1 | Standings + difficulty chart placement | **Separate page `/package/{id}/results`**; package header gets a prominent «Результати» link + secondary external links |
| 2 | Question-count mismatch | **Import standings, skip per-question stats for that source, warn the manager** (never misattach stats) |
| 3 | Shoot-out stats | **Store & display when the platform provides them** (own denominator, e.g. 1/2); warm-up never |
| 4 | Load mechanism | **Synchronous in manage UI with spinner** (1–3 fast HTTP calls); reload = same button; failure keeps old data |
| 5 | Per-question stat display | Inside the existing answer spoiler (`.answer-section`), after «Автор(и)» |
| 6 | Permissions | Attach/load/delete gated by existing `AccessControlService.CanEditPackage` (owner/admin) |
| 7 | Load-once guarantee | Raw platform JSON persisted (`jsonb`) — re-parse without re-fetch; refetch only on explicit «Оновити» |
| 8 | Rating auth | **Not used in v1** (reads anonymous; JWT doesn't bypass embargo). `.env` credentials stay reserved |
| 9 | Chart tech | **Server-rendered inline SVG** (no JS lib; the page is static SSR; fast & light) |
| 10 | Caching | Precomputed `QuestionStats` table only; no `IMemoryCache` in v1 (no invalidation complexity) |
| 11 | Enum values | `ResultsPlatform { Other = 0, Rating = 1, OpenQuiz = 2 }` — the loader-less «Other» is the safe default |
| 12 | Stats recompute | **Full re-aggregation** from all sources' persisted per-team data in one transaction — never incremental deltas |

**Non-goals (deferred)**: Shvager results, public API v1 exposure, `.qhub` export of results, per-question team-list UI (the data IS stored for it), auto-polling embargoed tournaments.

---

## Data model (one migration: `AddPackageResults`)

New enum `ResultsPlatform { Other = 0, Rating = 1, OpenQuiz = 2 }`.

### `ResultsSource` (`Domain/ResultsSource.cs`) — one attached link per package per play

- `Id`, `PackageId` (FK cascade; nav `Package.ResultsSources`), `Platform`, `Url` (as entered, max 2000), `Label` (nullable, max 200), `ExternalId` (nullable string, max 50), `DisplayUrl` (nullable, max 2000 — canonical public link for external display; for OpenQuiz built from the results token, for Other = `Url`)
- Load state: `LoadedAt`, `LastAttemptAt`, `LoadError` (max 1000), `ResultsAvailableAfter` (from Rating `hideResultsTo`), `TeamsCount` (nullable int), `StatsMapped` (bool), `WarningsJson`
- `RawPayload` (`jsonb`) — raw platform response
- `CreatedAt`. Index on `PackageId`.

### `TeamResult` (`Domain/TeamResult.cs`)

- `Id`, `ResultsSourceId` (FK cascade), `Name` (max 200), `Town` (nullable, max 200), `ExternalTeamId` (nullable int), `Points` (decimal — OpenQuiz can be fractional), `Position` (nullable decimal — source-reported)
- `ResultsByQuestionJson` (`jsonb`, nullable) — dict `QuestionId → 0|1` written at mapping time. Encoding rule: key present ⇒ team is in that question's denominator; `1` ⇒ correct. Regular questions get explicit `0` for played-but-missed; shoot-out questions omit non-participants; `?`/`X` mask chars and mask-less teams are omitted. Makes the future «which teams answered Q» feature a pure DB read. Index on `ResultsSourceId`.

### `QuestionStat` (`Domain/QuestionStat.cs`) — the precalculation that keeps the view page fast

- `QuestionId` (PK, FK cascade), `CorrectCount`, `TotalTeams`.
- **Recompute = full re-aggregation, never incremental.** On every load/reload/delete of any source, inside the same transaction: delete the package's `QuestionStats` rows and rebuild by scanning `ResultsByQuestionJson` of **all `TeamResults` of all the package's sources** (the reloaded source's rows were just replaced; other sources' rows are untouched and contribute again). A reload of platform A can never corrupt platform B's contribution, and a failed reload rolls back both team rows and stats.

---

## Subsystem designs

### 1. Platform clients (`Infrastructure/Results/`)

- `ResultsUrlParser` — static: recognizes Rating URLs (`rating.chgk.info/tournament/{id}[/...]`, bare id, api URL) and OpenQuiz URLs (`results.html?...quiz=&token=` res-link; `app/index.html?who=aud&quiz=&token=` aud-link) → `(platform, externalId, token, kind)`.
- `RatingResultsClient` — `IHttpClientFactory` named client `"ResultsLoader"` (30s timeout, `QuestionsHub/1.0` UA — the `PackageImportServiceExtensions` pattern). Fetches tournament info + results-with-masks; surfaces the embargo (`hideResultsTo` + null masks) as a typed error.
- `OpenQuizResultsClient` — res-link → fetch `results.json` directly; aud-link → login → `getQuiz` → `RT` → fetch; stores the canonical public results URL as `DisplayUrl`.
- Both return a common `PlatformResults` DTO: teams (name/town/points/position/external id) + per-team answer sequences + platform question descriptors (display name, play order, tour boundaries). Base URLs configurable via `ResultsOptions` (testability), registered in a new `AddPackageResults()` extension.

### 2. Mapping (`Infrastructure/Results/QuestionStatsMapper.cs`)

Package side: stat-eligible questions = questions of `TourType.Regular` tours in display order (tours by ЩДК sort, questions by `OrderIndex`), plus `TourType.Shootout` questions separately. Warm-up always excluded.

- **Rating**: mask index *i* → *i*-th regular question. Require `mask.Length == regularCount`, else standings-only + warning. Per char: `1`→1, `0`→0, `?`/`X`→omit from denominator (+warning for `?` — «нерозглянуті спірні»).
- **OpenQuiz**: platform questions in play order; `Name == "0"` → skip (warm-up); int-parsed `Name > 100` → shoot-out group; rest → regular group. Each group maps positionally when counts match and degrades independently (regular mismatch ⇒ standings-only; shoot-out mismatch ⇒ regular-only + warning). Teams with zero answers overall are standings-only.
- Extra sanity warning: per-tour counts (Rating `questionQty` / OpenQuiz `EOT` boundaries) compared against the package's tour sizes — divergence is flagged even when totals match.
- Output per team: `ResultsByQuestionJson` + warnings list.

### 3. Orchestration (`Infrastructure/Results/PackageResultsService.cs`)

`AttachSource` (parse URL, validate, insert), `LoadSource` (fetch → validate non-empty → map → in one transaction: replace `TeamResults`, update source fields + `RawPayload`, recompute `QuestionStats` for the package), `DeleteSource` (+recompute), `GetSourcesForPackage`, `GetResultsPageData`. Failure before commit ⇒ old data intact, `LoadError`/`LastAttemptAt` updated. «Other»: attach only, never loaded.

### 4. Public display

- `QuestionContent.razor` — new optional `Stat` parameter; after the «Автор(и)» field: `Взяли: 5 / 10 (50%)`. Rendered only when a stat exists ⇒ zero impact on packages without results.
- `PackageDetail.razor` — header block (after «Зіграно»): when any loaded source has teams → `Результати` primary button linking to `/package/{id}/results` + muted team count; external links (`DisplayUrl`) as small secondary links with platform name, `target="_blank" rel="noopener noreferrer"`, external-link icon. Data: two cheap extra queries (sources by `PackageId`; `QuestionStats` by the package's question ids → dictionary passed down `QuestionCard`→`QuestionContent`).
- `QuestionDetail.razor` — same stat line (single PK lookup).

### 5. Results page (`Components/Pages/PackageResults.razor`, route `/package/{Id:int}/results`)

**InteractiveServer** (revised from static SSR — the source selector needs live recompute), same `CanViewPackage` gate.

- **Source selector** (when >1 loaded source): a checkbox per source (all checked by default = aggregated view); toggling recomputes both the chart and the standings in-memory for the selected subset via `QuestionStatsAggregator` + `StandingsBuilder`. One source → no selector. Two tournaments of the same platform (e.g. a package played in two languages) are distinct sources and separately toggleable.
- **Difficulty chart** («Складність запитань»), shown first: inline SVG in `DifficultyChart.razor` — one bar per stat-eligible question, height = % correct, alternating tour background bands + tour labels, % scale, native `<title>` tooltips, bars link to `/package/{id}#question-{qid}`, zero-correct bars tinted red.
- **Standings**: teams from the selected sources, re-ranked by `Points` desc (ties share place, shown «3–4»); columns: Місце | Команда | per-tour correct counts | «П» shoot-out | Разом. City is **not** displayed (kept in DB). Table in an `overflow-x: auto` wrapper.
- Empty states: no loaded sources → «Результатів ще немає»; none selected → «Оберіть хоча б одне джерело».

*(Revised per user feedback after v1: chart moved above the table; source column dropped; icon-only source summary; InteractiveServer + source selector; source-icon tooltips show the fetched tournament title.)*

### 6. Management UI (`ManagePackageDetail.razor`)

New card «Результати» between the package-info card and the «Тури» card:

- Rows per source: platform badge, URL, status (Завантажено: N команд, дата / Помилка + message + collapsed technical detail / embargo date / Не завантажується (Інше)), warnings from `WarningsJson`, «Оновити» (spinner while loading; hidden for Other) and delete (hand-rolled confirm modal, house pattern).
- Adding a source: single inline URL field (no modal, no label) — the platform is auto-detected from the link; on add: attach, then for Rating/OpenQuiz immediately `LoadSource` with spinner. All behind the page's existing `CanEditPackage` gate. *(Revised per user feedback after v1: originally a modal with platform select + label.)*

---

## Phased plan (live checklist)

Each phase is independently buildable with green `dotnet test`. Commits per phase on explicit command.

- [x] **Phase 0 — This plan doc**
- [x] **Phase 1 — Schema & domain**: 3 entities + enum, DbContext config (jsonb, indexes, cascades), migration `AddPackageResults`, `Package.ResultsSources` nav
- [x] **Phase 2 — Platform clients & parsers**: `ResultsUrlParser`, both clients, `PlatformResults` DTOs, `ResultsOptions` + `AddPackageResults()`. Tests on committed real fixtures (13097, 25795, 26130); embargo & empty cases
- [x] **Phase 3 — Mapping & orchestration**: `QuestionStatsMapper` + `PackageResultsService`. Tests: exact/mismatch/shootout/`?`/`X`/zero-answer-team mapping; attach/load/reload-failure-keeps-old/delete + recompute
- [x] **Phase 4 — Management UI**: «Результати» card, add-source modal, sync load/reload/delete, warnings
- [x] **Phase 5 — Public display**: stat line in `QuestionContent`/`QuestionDetail`, header links + stat dictionary in `PackageDetail`. Deviation: `DisplayUrl` is now also set at attach time (Rating site page, OpenQuiz res-link) so external links appear before the first load
- [x] **Phase 6 — Results page**: standings table, `DifficultyChart.razor` + `DifficultyChartModel`/`StandingsBuilder` (with tests), empty states
- [x] **Phase 7 — Docs & polish**: `docs/RESULTS.md`, `CLAUDE.md` doc table, `SITE_SPECIFICATION.md`; adversarial code review + end-to-end verify

## Verification (end-to-end)

- `dotnet build` + full `dotnet test`.
- Headless run per `docs/LOCAL_DEV.md`: attach Rating 13097 to a 36-regular-question package → 17 teams, Q11=14/17, Q6=0/17; attach OpenQuiz 25795 (both res-link and aud-link forms) → 44 teams, shoot-out 101–105 stats present; attach an «Other» link → external link only.
- Negative paths: reload with network cut (old data intact + error shown), embargoed Rating tournament → embargo message, mismatched package → standings-only + warning.
- Curl-smoke `/package/{id}` (stat lines, «Результати» button) and `/package/{id}/results` (table + SVG) as anonymous user.

## Risks & notes

- **OpenQuiz is unofficial** — URL scheme may change on redeploy. Mitigations: raw payload persisted, loader isolated in one client class, errors never destroy old data.
- **Positional mapping is trust-based** — a package whose question order differs from the platform's play order maps wrong silently when counts match. Mitigation: per-tour count comparison warnings (see Mapping).
- **`ManagePackageDetail.razor` monolith** grows further — keep logic in the service; markup only in the page.
- Question edits after load: stats stay attached to `QuestionId` (renumbering/reordering keeps stats with the question's content); deleting a question cascades its stat; reload re-maps against the current structure.
