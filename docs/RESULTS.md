# Tournament Results & Question Statistics

How Questions Hub loads tournament results from external platforms, computes per-question
statistics, and displays them. Design history and the phased plan live in
[Stats_plan.md](Stats_plan.md). Що?Де?Коли? (WWW) packages only — Shvager is not supported.

## Concepts

- **ResultsSource** — one attached link per play of a package on a platform
  (`ResultsPlatform`: `Other = 0`, `Rating = 1`, `OpenQuiz = 2`). Rating/OpenQuiz sources are
  loaded into the DB; «Other» is display-only. A package may have several sources (played on
  2–3 platforms, or twice on one platform); statistics are summed over all of them.
- **TeamResult** — a team's standing within a source (name at tournament time, town, points,
  source-reported position, platform team id via `ExternalTeamId`, and `ResultsSourceId` →
  platform) plus `ResultsByQuestionJson`: a jsonb dict `QuestionId → 0|1`. Key present ⇒ the
  team is in that question's denominator; `1` ⇒ correct. Shoot-out non-participants and Rating
  `?`/`X` mask positions are omitted; teams without per-question data have `null`. `Town` and
  `ExternalTeamId` are stored but the town is **not displayed** — kept for a future
  «all results of a team» page (queryable by `ExternalTeamId` + source platform). A team can't
  play the same package twice within one tournament, but the same name can recur across
  sources, so team rows are never deduplicated by name.
- **QuestionStat** — precomputed `CorrectCount`/`TotalTeams` per question (PK = `QuestionId`),
  aggregated over **all** sources. Rebuilt by full re-aggregation inside the load/delete
  transaction — never incrementally — so reloading one platform can't corrupt another's
  contribution. This precalculation keeps the package view page fast; the results page, which
  lets the user filter sources, recomputes on the fly instead (see below).

## Loading pipeline (`Infrastructure/Results/`)

> **Transactions must run inside an execution strategy.** The app enables
> `EnableRetryOnFailure` (Program.cs), so a bare `BeginTransactionAsync` throws
> `InvalidOperationException`. `LoadSource`/`DeleteSource` wrap their transaction in
> `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` (same as `PackageDbImporter`) and
> clear the change tracker at the start of each attempt so a retried delegate is re-entrant.
> InMemory unit tests do **not** exercise the retrying strategy — this path needs a real-Postgres check.

`PackageResultsService.AttachSource` validates the URL (`ResultsUrlParser`), rejects
duplicates and non-WWW packages. `LoadSource` (synchronous, triggered from the manage UI):

1. Re-parses the stored URL (tokens are not stored separately — the URL is the source of truth).
2. Fetches via the platform client (named HttpClient `ResultsLoader`, 30 s timeout).
3. Maps platform answers onto package questions (`QuestionStatsMapper`).
4. In one transaction: replaces the source's `TeamResults`, updates the source
   (`LoadedAt`, `TeamsCount`, `StatsMapped`, `WarningsJson`, `RawPayload`, `DisplayUrl`),
   re-aggregates `QuestionStats`.

Any failure before the commit leaves previously loaded data intact; the error lands in
`ResultsSource.LoadError` (embargo date in `ResultsAvailableAfter`). The raw platform JSON is
persisted (`RawPayload`, jsonb) so results can be re-parsed after code changes without
re-fetching («load external results only once»); a refetch happens only on explicit «Оновити».

### Rating (rating.chgk.info)

- Official API, **anonymous** (auth exists but does not bypass the results embargo, so it is
  not used). Base URLs in `ResultsOptions` (`Results` config section).
- `GET /tournaments/{id}` → `questionQty` (per-tour counts), `hideResultsTo` (embargo).
- `GET /tournaments/{id}/results?includeMasksAndControversials=1` → per team: `team.name`,
  `town`, `position` (float, ties share averaged), `questionsTotal`, `mask` — one char per
  question in play order: `0` wrong, `1` correct, `?` pending controversial, `X` removed
  question. `?`/`X` are excluded from stats denominators (with a warning for `?`).
- **Embargo**: until `hideResultsTo`, masks/positions/points are `null` (team list visible).
  Surfaced as a typed error with the date; the manager reloads after it passes.
- Accepted URL forms: `https://rating.chgk.info/tournament/{id}[/...]`,
  `https://api.rating.chgk.info/tournaments/{id}`, or a bare tournament id.

### OpenQuiz (open-quiz.com)

- **Unofficial** (no API docs; reverse-engineered from github.com/usix79/openquiz). Results are
  a static file: `GET /static/{quizId}-{resultsToken}/results.json` (`?nocache={ticks}` to skip
  the CDN). The `token` of a public results link IS the results token.
- An audience link (`app/index.html?who=aud&quiz=…&token=…`) carries a listen token instead;
  it is exchanged via the Fable.Remoting API: `POST /api/ISecurityApi/login` (body
  `[{"Token":"","Arg":{"AudUser":{"QuizId":N,"Token":"…"}}}]`) → JWT →
  `POST /api/IAudApi/getQuiz` → `RT` (results token) + `QN` (quiz name). The public results
  URL is then stored as `DisplayUrl` — the aud link itself is never displayed.
- results.json shape: `Questions[]` in play order (`Name` display string, `EOT` = last
  question of a tour) + `Teams[]` (`TeamName`, `Points` decimal, `PlaceFrom/To`, `Details` map
  keyed by **stringified compact JSON** `{"TourIdx":0,"QwIdx":0}` → `{Result}`; `Result > 0`
  correct, `null`/`0` wrong, key absent = no answer submitted). `Questions[].Score` is a
  difficulty score, **not** the correct count.
- Question classification by `Name`: `"0"` → warm-up (skipped), integer ≥ 100 → shoot-out,
  anything else (including `"8.1"` slips) → regular.

## Mapping rules (`QuestionStatsMapper`)

Positional, in two independent groups:

- **Regular**: i-th platform regular question ↔ i-th question of the package's `Regular` tours
  (tours in ЩДК display order, questions by `OrderIndex`). Count mismatch ⇒ that source loads
  **standings only** + warning — stats are never misattached (safer than best-effort).
- **Shoot-out**: mapped the same way onto `Shootout` tours when the platform provides such
  questions; degrades independently of the regular group. `NoAnswer` on a shoot-out question
  means «did not participate» (out of the denominator); on a regular question it counts as a miss.
- Warm-up questions never get stats (either side).
- Extra sanity warning when the per-tour breakdown differs between platform and package even
  though the totals match (guards against silently shifted numbering).

## UI surfaces

- **Home page** (`Home.razor` + `home-filters.js`): each package card shows a stats bar-chart icon
  (`Icon Name="bar-chart"`, tooltip «Є статистика») when the package has at least one loaded source
  (`LoadedAt != null && TeamsCount > 0` — same gate as the detail page's «Результати» button).
  The whole card links to the package page, so the icon can't be a nested `<a>`; instead it is a
  `.package-stats-link` span whose click is intercepted (`setupPackageCardClicks`, same mechanism
  as the tag badges) to navigate to the results page (`/package/{id}/results`).
  Computed as `PackageCardDto.HasResults` via an `EXISTS` on `ResultsSources` in
  `PackageListService.SearchPackages`; also surfaced in the public list API (`hasResults`). The
  card is rendered twice — server-side in the Razor and client-side in `renderPackageCard` — so
  both templates must stay in sync.
- **Package page** (`PackageDetail.razor`): header gets a primary «Результати» button
  (internal page, preferred) + secondary external icon links — a shared `PlatformIcon`
  component renders each platform's favicon (`wwwroot/images/platform-*`) or a generic
  external-link icon, linking to `DisplayUrl` in a new tab. Each icon's tooltip is the fetched
  tournament title (`ResultsSource.Title`), falling back to the platform name. Each question
  shows «Взяли: X/Y (Z%)» inside the answer spoiler after the authors (only when a stat exists).
- **Results page** (`/package/{id}/results`, `PackageResults.razor`, **InteractiveServer** — it
  needs live recompute; same access gate): loaded-source platform icons (tooltip = tournament
  title). When a package has more than one loaded source, a **source selector** (checkbox per
  source, all checked by default) lets the user narrow the view; the difficulty chart **and**
  the standings recompute in-memory for the selected subset (`QuestionStatsAggregator` +
  `StandingsBuilder` over the selected sources' team rows — the persisted `QuestionStat` table
  is not used here). One source → no selector. The chart comes first (server-rendered inline
  SVG, `DifficultyChart.razor` + `DifficultyChartModel`: one bar per question % correct,
  alternating tour bands, bars link to question anchors, zero-correct = red sliver), then the
  standings re-ranked by points across the selected sources (ties share «3–4», per-tour correct
  counts, «П» shoot-out column; city not shown). Use case: the same package played in two
  languages is two Rating tournaments = two distinct sources, so an editor can compare
  per-language question difficulty by toggling.
- **Manage page** (`ManagePackageDetail.razor`, «Результати» card): a single URL field — the
  platform is auto-detected from the link (`ResultsUrlParser.Detect`); different URL forms of
  one tournament (…/13098, …/13098/statistics, …/13098/tours) dedupe by external id.
  Synchronous load/reload with spinner, delete with confirmation, status line and mapping
  warnings per source; failed loads expose a collapsed «Технічні деталі помилки» section
  (`ResultsSource.LoadErrorDetail`, the stored exception text). Gated by `CanEditPackage`
  (owner/admin).

## Debugging

- `ResultsSources.RawPayload` (jsonb) holds the exact platform response of the last successful
  load — re-parse offline instead of re-fetching.
- `WarningsJson` — user-facing mapping/load warnings shown on the manage page.
- Unit-test fixtures under `QuestionsHub.UnitTests/TestData/Results/` are real captures
  (Rating tournament 13097; OpenQuiz quizzes 25795 incl. shoot-out, 26130 incl. warm-up) —
  golden values in the tests were cross-checked against the live sites.
- Reproduce a load in a test via `FakeResultsHandler` (routes URL substrings to fixture bodies).

## Risks & limits

- OpenQuiz is unofficial: a redeploy may change the URL scheme. The loader is isolated in
  `OpenQuizResultsClient`; failures never destroy loaded data.
- Positional mapping trusts play order; the per-tour warning is the tripwire. When in doubt the
  manager sees a warning and stats are simply absent, never wrong.
- Question edits after a load: stats stay attached to the `QuestionId` (renumbering keeps stats
  with the question); deleting a question cascades its stat; a reload re-maps against the
  current structure.
- No rate limiting observed on either platform; loads are manual and 1–3 requests each.

## Deferred

Shvager results, public API exposure of stats, `.qhub` export of results, per-question team
list UI (data already stored), auto-polling embargoed tournaments.
