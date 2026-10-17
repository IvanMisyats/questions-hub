# Package import & `.qhub` export through the Agent API — Implementation Plan

Branch `feature/qhub-transfer`, started 2026-10-16 with the `/feature` flow. Follows the sketch in
[AGENT_API_PLAN.md](AGENT_API_PLAN.md) "Next feature". This file is the source of truth: decisions,
design, phase checklist and notes.

## Goals

1. **"Upload this package."** A user's agent (Claude Code, Codex) uploads a local DOCX or `.qhub`
   as a Draft, reads the parser's warnings, reviews the draft against the source and fixes it with
   changesets, then attaches tournament results.
2. **Format-level backup.** A scheduled script downloads every package as `.qhub` and keeps the
   copies current, downloading only what changed. Complements the server backups
   ([BACKUPS.md](BACKUPS.md)); does not replace them.

## Decision Log

| # | Decision | Why |
|---|----------|-----|
| 1 | **File bytes travel over REST, MCP orchestrates.** Upload `POST /api/v1/manage/imports` (multipart), download `GET /api/v1/manage/packages/{id}/export`. MCP gets job status/list tools and the format spec; tool texts show the `curl` commands. | MCP tool arguments are text the model writes: a DOCX with pictures cannot pass through a tool call. JSON-RPC bodies are capped at 512 KB anyway. A backup script needs no model. |
| 2 | No MCP-issued transfer links (yet). The agent's shell uses the token for the two `curl` steps. | Simplest. Links (short-lived, single-use, no token in the shell) can come later if other editors need them. |
| 3 | **Incremental export via `ETag`/`If-None-Match`**, no "last modified" column. Weak ETag over the generated `package.json` **plus the length and modification time of every referenced local media file** (a missing file counts as "missing"). | `.qhub` holds no status/access fields, and media files get fresh random names when replaced, so manifest + file stats capture the exported content: a restored or missing file changes the tag. Avoids hunting every save path to bump a timestamp. Immutable-media contract: rewriting a file in place with the same length and mtime is not detected. |
| 4 | No dry-run import. An import creates a **Draft** (invisible to the public); the agent reviews and fixes it with changesets. A hopeless parse — or missing media/blocks, which changesets cannot create — means: convert offline ([QHUB_CONVERSION.md](QHUB_CONVERSION.md)), upload the `.qhub`, and the user deletes the broken draft in the UI. | The draft *is* the preview, in the format agents already read (`get_package`). No delete or publish through the API. |
| 5 | Enqueueing needs a `readWrite` token **without a package allowlist**; job status/list need an unrestricted token of any scope. Jobs are visible only to their owner, admins included. No new token permission. | A package that does not exist yet cannot be on an allowlist. Job lists show file names of the user's other imports, which a restricted token must not see. |
| 6 | Import accepts `.docx` and `.qhub` (same pipeline as the UI: `PackageImportService` → background job). `gameType` (`www`/`shvager`) is required; for `.qhub` the file's own type wins (existing warning). The DTO calls it `requestedGameType`. | The server parser handles normal layouts; `.qhub` covers what it can't. A required type keeps the "type differs from the file" warning meaningful. |
| 7 | Token id of an import is logged, not stored. | Nothing would read a stored value; avoids a migration. |
| 8 | Budgets: `agent.export` 60/min per token; `agent.import` 10/hour **per user** (more tokens don't multiply it); ≤ 3 unfinished imports per user (queued, running, or waiting for a retry), checked and enqueued atomically under a per-user lock **before** the body is read. | Exports read media from disk; each upload stores up to 50 MiB and occupies the job queue. Single-instance app, so an in-process lock is enough. |
| 9 | Import size limits are hardened first (phase 1), calibrated on 280 real DOCX files. Limits live in `PackageImportOptions` (configurable). | Tokens make the import pipeline scriptable. |
| 10 | Rating search stays in `tools/rating/find_tournaments.py`. | Fuzzy matching (names, transliterated editors, sibling tournaments) is large to port and only one workflow uses it. Backlog. |
| 11 | Ivan's workflows (the upload skill, the backup script and its scheduled task, registering this MCP in Claude Code) live in his dotfiles, not here. | Personal paths, tokens and conventions; the repo keeps the API, the format spec and the matching conventions ([RATING_SITE.md](RATING_SITE.md)). |
| 12 | A `.qhub` must round-trip: export → import reproduces the content. Tour comments (exported but dropped on import today) are fixed. Intentional exclusions: ownership, status/access level, publication date, tournament results, and the bytes behind external media URLs (an import downloads them; a dead link is dropped with a warning, as before). | Otherwise the backup silently loses content. |
| 13 | The upload POST is not idempotent. A client that lost the `202` reconciles through `GET /imports` before retrying. | An idempotency key is not worth it at 10 uploads/hour. |

## Design

### Export — `GET /api/v1/manage/packages/{id}/export`

- `ManageExportController` (`Controllers/Api/Manage/`), policy `AgentPolicies.Read`, any scope; the
  package must be editable by the agent (`EditablePackages` + `CanEditAsAgent`; 404 otherwise). Budget
  `agent.export` (an action/controller-level `ClientRateLimit` replaces, not stacks with, `agent.read`).
- Loading: `PackageGraph.WithEditableGraph()` inside a Repeatable Read transaction (same as
  `AgentPackageReader.LoadPackage`), so a concurrent edit cannot produce a mixed export. The exporter reads
  block questions through `PackageGraph.BlockQuestions` / `RootQuestions` (the editable graph does not
  populate `Block.Questions`). The cookie endpoint (`PackageExportController`) keeps its contract and
  switches to the same loader.
- `QhubExporter` is split: `BuildManifest(package)` → `package.json` bytes + the asset list (authors,
  editors ordered by id, tags by name, so the bytes are canonical); `Export` writes them.
- `ETag: W/"<sha256>"` over a revision prefix, the manifest bytes and each local asset's
  `(name, length, mtime | missing)`. `If-None-Match` is parsed as a list (`*` supported, weak comparison);
  rights are checked before a `304`. `Cache-Control: private, no-cache` on 200 and 304. Missing local
  files are skipped as today and counted in an `X-Qhub-Missing-Assets` header (and logged).
- The archive is built in a temp file (`DeleteOnClose`) and handed to MVC as a `FileStreamResult`
  (MVC disposes it). The shared graph gate (4) is held only while loading and building; a separate gate
  (2) bounds outstanding export files until their stream is disposed (503 Busy + `Retry-After: 5`).
  Request cancellation flows through loading and copying; the temp file is disposed on every failure
  before the result is returned.

### Import — `/api/v1/manage/imports`

| Method & path | Token | Purpose |
|---|---|---|
| `POST /imports` (multipart `file`, `gameType`) | readWrite, unrestricted | Enqueue an import job → `202` with the job |
| `GET /imports/{jobId}` | unrestricted | One of the user's jobs |
| `GET /imports` | unrestricted | The user's latest 20 jobs, newest first |

- Job DTO: `jobId, fileName, requestedGameType, status (queued/running/retrying/succeeded/failed),
  currentStep, progress, createdAt, startedAt, finishedAt, nextRetryAt, packageId, errorMessage,
  warnings[]` — never input paths or `ErrorDetails`. `retrying` uses the worker's own eligibility
  predicate (failed, `NextRetryAt` set, attempts left). An owner-filtered query shared by REST and MCP
  (not the unscoped `GetJobsByIds` / admin-wide helpers).
- The action takes no form parameters: it checks scope, allowlist and the unfinished-job limit under the
  per-user lock, then reads the form itself (`MultipartBodyLengthLimit`, `RequestSizeLimit` 55 MiB;
  file ≤ 50 MiB). Errors: malformed multipart / missing field → 400; file checks (extension, size,
  empty) → 422; transport size → 413; all JSON. Partially saved inputs are removed if enqueueing fails.
- Job lifecycle normalized: claiming a job clears `NextRetryAt`/`FinishedAt`; terminal outcomes clear
  `NextRetryAt`; a cancelled job is persisted with an independent short cleanup token.
- The rate limiter gets a window per budget (`RateLimitWindows`), and the 429 `Retry-After` follows the
  budget's window. Settings bind under `RateLimits` (`AgentExportPerMinute`, `AgentImportPerHour`).
- nginx: `location = /api/v1/manage/imports` (and the trailing-slash form) with
  `client_max_body_size 55m`, the same rate limits/proxy settings as `/api/v1/manage/`, and its own
  `error_page 413/429` directives (sibling locations don't inherit them). Cloudflare Free allows 100 MB;
  its own edge errors are not the app's JSON.

### MCP

- New tools: `list_imports`, `get_import` (`jobId`), `get_qhub_format` (the `.qhub` spec
  [PACKAGE_FORMAT.md](PACKAGE_FORMAT.md) + JSON schema, embedded at build time; also a resource).
- `list_packages` gets `search` (title contains, case-insensitive) — the duplicate check before an upload.
- Server instructions: upload/download are REST (`curl`), the upload is not idempotent, review the draft,
  fix structure **before** attaching results (results block structural changesets), media and blocks
  cannot be created by changesets.

### Import hardening

Enforced on bytes actually read, not on declared sizes; configurable in `PackageImportOptions`.
Calibration (280 DOCX files from real packages): largest `document.xml` 1.2 MB / 37k elements, depth 20,
total expanded 47 MB, 72 entries, largest image 8 MB, highest legitimate XML compression ratio 82×.

| Limit | `.qhub` | DOCX |
|---|---:|---:|
| Archive entries | 1 024 | 1 024 |
| Total expanded | 100 MiB | 100 MiB |
| JSON / XML | `package.json` 4 MiB | main part 4 MiB (`MaxCharactersInPart`), other XML 1 MiB each, all XML 8 MiB; depth 64, 200 000 elements (streaming pre-scan) |
| One binary asset | 50 MiB (site videos are ≤ 50 MB) | 50 MiB |
| All extracted + downloaded assets | 100 MiB | 100 MiB, images deduplicated by part |
| Compression ratio | 100× | 500× for XML (the absolute caps do the work) |

Plus: cyclic/deep style inheritance, repeated downloads of one URL, quadratic numbering scans,
cancellation propagation (no catch-all swallowing `OperationCanceledException` or limit errors),
cleanup of partial files on failure, `MaxConcurrentJobs` = 1.

## Phased Plan

Codex (`gpt-6-astra`, effort high) collaborates: reviewed this plan, drafts phases 1 and 3 (Claude
reviews, extends, tests and commits), Claude drafts phases 2 and 4; Codex adversarially reviews every
phase diff and the whole branch. Each phase runs its related tests before committing.

- [x] **Phase 0** — branch, this plan, Codex plan review → revised (see notes).
- [x] **Phase 1** — import hardening + unit tests with crafted archives (Codex drafted, Claude reviewed and fixed, Codex re-reviewed).
- [x] **Phase 2** — export endpoint (loader, ETag/304, temp file, gates, headers), exporter refactor,
      tour-comment round-trip fix + round-trip tests for both game types, `agent.export`, `list_packages`
      search (REST and MCP). Unit + Postgres integration tests (blocks + media). Format 1.2 (people objects).
- [x] **Phase 3** — import endpoints, job lifecycle normalization, per-user admission + budget, windowed
      limiter, nginx location, REST docs; integration tests (DOCX and `.qhub` through the worker; 400/413/422;
      restricted/read tokens; foreign jobs) (Codex drafted).
- [x] **Phase 4** — MCP tools (`list_imports`, `get_import`, `get_qhub_format` + resource), server
      instructions, remaining docs (API.md agent section, PACKAGE_IMPORT.md, BACKUPS.md, CLAUDE.md); MCP tests.
- [x] **Phase 5** — verification: full build + tests, local app (DOCX and `.qhub` uploads, export 200 → 304,
      refusals), Codex whole-branch review. The fresh-agent MCP acceptance test is still to do (blocked, see notes).
- [ ] **Phase 6** (outside the repo, after deploy) — Ivan's dotfiles: register this MCP in Claude Code,
      the upload skill, the backup script + scheduled task with its own read-only token; first runs.

## Deployment checklist

- nginx: the import location (`location ~ ^/api/v1/manage/imports/?$`: 55m, `proxy_request_buffering off`,
  own `error_page 413/429`) from `infra/nginx/questions.com.ua.conf`, deployed by hand on the live box (strip a BOM
  if the file has one); `nginx -t` + reload.
- Appsettings (optional, defaults in code): `RateLimits:AgentExportPerMinute`, `RateLimits:AgentImportPerHour`,
  `PackageImport:*` limits.
- `ASPNETCORE_TEMP` / the container temp directory must hold buffered uploads (≤ 55 MiB each) and export
  archives (≤ 2 at a time).

## Deferred / follow-ups

- MCP-issued short-lived transfer links (decision 2).
- Server-side rating tournament search (decision 10).
- `validate_package` (parse a `package.json` without importing).
- Updating an existing package from a `.qhub`; media through the API.
- Retention of import job folders (`uploads/jobs/*` is never cleaned, UI imports included).

## Phase Notes

### Phase 0 — Codex plan review (2026-10-16)

Verdict "revise plan first". Accepted and folded in: the exporter/graph-loader mismatch (block questions);
Repeatable Read export; ETag must cover media presence (chose file stats over failing exports, so one
lost file doesn't stop a package's backups); canonical ordering; tour comments lost on round-trip;
atomic per-user admission incl. retry-pending jobs, before buffering; lifecycle fields
(`NextRetryAt` not cleared, cancellation saved with a cancelled token); 400/413/422 split and nginx
`error_page` inheritance; gate only while building + a bound on outstanding temp files; `Retry-After`
per window; the settings section is `RateLimits`; hardening scope beyond ZIP sizes (style cycles,
repeated images and downloads, cancellation, complexity). Limits table adjusted after measuring real
files: XML ratio 500× instead of 100× (a real handout DOCX is 82×). .NET's Deflate reader caps output
at the declared length (truncates); stored entries are bounded by the compressed length — so counters
stay, but declared sizes are not trusted either way.

### Phase 1 — Codex implementation (2026-10-16)

Implemented configurable limits, declared-size checks and shared actual-byte budgets, DOCX XML
pre-scan before OpenXML, bounded style inheritance, running list counters, per-import URL/image
deduplication, cancellation propagation and partial-file cleanup. Parser association now tracks
image occurrences by reference instead of filename so a shared file can still serve both handout
and comment references in one paragraph; the DB importer already deduplicates files by name.

Added 47 crafted-archive tests, including lying stored-entry lengths, Deflate truncation, ZIP64
overflow, XML complexity/DTD, image association and cancellation during copies. Final verification:
`dotnet build --no-restore` succeeded (0 errors, 32 analyzer warnings);
`dotnet test QuestionsHub.UnitTests --no-restore` passed: 1,459 passed, 2 skipped, 1,461 total.
Plain restore-enabled build is blocked by sandbox access to the user-level NuGet.Config; dependencies
were unchanged. Docker integration tests were not run. No commits, branches, pushes or migrations;
Phase 2/export/tour-comment and Phase 3/job-lifecycle code left untouched.

### Phase 1 — Claude review and Codex adversarial review (2026-10-16)

Claude's review changed: an external `.qhub` image over the per-file limit or slower than 30 s is a
warning again, as before (the draft failed the whole import); `MaxAssetBytes` 50 MiB, because the site
itself accepts 50 MB videos and exports must re-import; a central-directory pre-check
(`ExtractionLimits.CheckCentralDirectory`) before `ZipArchive` materializes one object per directory
record (a 50 MiB upload can hold about a million).

Codex's review of that found, all fixed: ZIP64 triggered by a disk-number sentinel, or a ZIP64 record
inside the comment, bypassed the pre-check → strict ZIP64 handling (any sentinel requires a valid locator
and record, else reject); XML parts named other than `.xml` (a styles part `styles.bin` declared by content
type) skipped the XML scan, and main-part detection by name was case-sensitive → the parts the extractor
loads as a DOM (main document, styles, numbering) are scanned after `Open` and before any DOM access, as
OpenXML resolves them; a per-file limit tripping first hid bytes from the aggregate budget → budgets are
charged in order, aggregate first.

Verified: 1465 unit tests; a harness ran the pre-phase and the new DOCX extractor + both parsers over the
280 real DOCX packages in Ivan's OneDrive and compared the output (asset names normalized to content
hashes): 0 differences, 0 rejections.

Codex's second look at those fixes found, also fixed: the runtime follows ZIP64 only for the disk-number,
entry-count and offset sentinels, so a directory-size sentinel alone keeps the ordinary values in use —
the pre-check now bounds whichever values the runtime will use; a DOM-loaded part with another extension
now also counts towards the XML total and gets the XML compression ratio (`DocxArchiveValidator.Precheck`).
Final: 1468 unit + 97 integration tests; the 280-file regression unchanged (0 differences, 0 rejections).
The last two fixes go into the whole-branch Codex review instead of a third round.

### Phase 2 — export (2026-10-16, Claude; Codex review)

Built in a separate worktree while Codex drafted phase 1. Deviations from the design: `list_packages`
got `search` on MCP too (one line); the `.qhub` format went to **1.2** (below).

Found while building: Npgsql's two-argument `EF.Functions.ILike` emits `ESCAPE ''`, so
`PackageListService`'s escaped wildcards never worked (`%` in a title search matched nothing) — the escape
character is now explicit, which also fixes the site's title search. The round-trip test showed tour
comments were dropped on import (fixed, as planned).

Codex's review (verdict "commit after fixes"), all fixed:
- Authors/editors whose first or last name has several words were dropped on import (the string form
  must be two words; only a log line said so) → format 1.2: such a person is `{firstName, lastName}`
  (`QhubPeople`, converter on the people lists; the pipeline carries the exact pair with a U+001F
  separator); unparsable names become job warnings.
- `TagService.GetOrCreate` used `ILike`, so restoring tag `100%` could reuse `100 років` → literal
  case-insensitive equality (as the changeset engine); Postgres test.
- Exports must re-import: media entries are stored, not deflated (a WAV of silence would trip the 100×
  ratio); archives over the import limits (50 MiB file, 100 MiB unpacked) are documented as needing
  the `PackageImport` limits raised.
- A failed temp-file creation kept its archive slot until finalization → an idempotent `SlotLease`
  handed to the file only once it exists; the stream is also registered for response disposal.
- Exports waited 10 s for an archive slot while holding a graph-read slot, starving `get_package` →
  archive slots are taken without waiting (503).
- An early 304 ignored a failing `If-Match` → with `If-Match` present MVC evaluates the preconditions (412).

Tests: QhubExporterTests, QhubRoundTripTests (both game types, multi-word author, `100%` tag), QhubPeopleTests,
ManageExportHttpTests (200/304/changed/missing media/412, rights), ManageReadTests search,
TagServicePostgresTests.

### Phase 3 — imports (2026-10-16, Codex draft; Claude review + code-review skill)

Codex drafted the endpoints, `AgentImportService`, `ImportJobLifecycle`, windowed budgets (a window per
budget, `Retry-After` from it), the 413 mapping for wrapped multipart size errors, lifecycle fixes (claim
clears retry/finish fields, terminal outcomes clear `NextRetryAt`, cancellation persisted with its own
token and rethrown so the worker tells timeout from shutdown, Enqueue cleans up its folder on failure),
nginx, docs and 20 unit + 18 integration tests (its sandbox could not reach Docker; Claude ran them).

Claude's review: reverted an unrequested UI change (the import section listing the last 20 imports
forever; it shows active, retry-pending and this session's jobs as before). The code-review skill (a
fresh Claude reviewer) then found, all fixed:
- the hourly `agent.import` permit was charged by middleware before any check, so refused uploads used it
  up → charged by the service only for admitted uploads (the request counts against `agent.read`);
- the per-user lock was held for the whole upload (and its stripes shared between users) → admission is a
  short critical section that reserves a slot (saved unfinished jobs + uploads in flight ≤ 3); the body is
  read outside it, within 10 minutes (408 otherwise);
- a retry scheduled by a retriable error racing a cancellation was overwritten as a permanent failure →
  the worker's failure marking keeps a scheduled retry;
- any stray `OperationCanceledException` read as a timeout → only the job's token counts as cancellation,
  the worker's timeout filter checks its own timer; other cancellations are ordinary failures;
- the job row was saved with the request's token after the file was written (a disconnect could leave a
  committed job without its input) → saved non-cancellably;
- one set of lifecycle expressions (retry-pending, unfinished, due) instead of three copies and UNIONs;
  owner-scoped job lookups in `PackageImportService`; one regex nginx location.

### Phase 5 — verification and the whole-branch review (2026-10-16)

Local app (real Kestrel, dev Postgres), with tokens minted in the dev DB: a ЩДК DOCX (1.2 MB of XML, 90
questions) and a Своя гра DOCX imported through the REST upload with Cyrillic file names; export 200 →
304; the exported `.qhub` re-imported and re-exported to an identical `package.json` (10/10 media by hash);
refusals 403/400/422/413 (JSON from Kestrel for a 56 MiB body)/404; uppercase Cyrillic title search. A
backup client ran three times over the 159 dev packages: first run 429s exposed its own Retry-After bug
(PowerShell unwraps `Nullable<TimeSpan>`), then 25 new + 134 × 304, then a rename (new file, old one gone)
and a deletion (moved to `_removed`). Git Bash's curl cannot open Cyrillic paths: agents use Windows'
`curl.exe` (noted in the skill). The fresh-agent MCP acceptance test was stopped by Claude Code's
auto-mode classifier (a token handed over by the parent agent is not the user's approval) — rerun with
Ivan's approval.

Codex's whole-branch review ("merge after fixes") — fixed:
- Memory amplification through diagnostics (a long tour number repeated in every question's warning; a long
  DOCX numbering label repeated per paragraph) → labels capped (100 chars, which keeps generated asset names
  whole), at most 500 warnings plus a count, numbering prefixes ≤ 32 chars (`ImportDiagnostics`, `ImportWarnings`).
- nginx buffered whole uploads before the app's checks → `proxy_request_buffering off` on the upload route.
- A tour with both `questions` and `blocks` lost its questions silently → rejected; docs and schema say so.
- An export could fail its own re-import (repetitive `package.json` deflated past 100×) → every entry stored.
- An error message over the 1000-char column left a job "running" → messages bounded, details kept.
- A cancelled import could leave copied media (rollback with a cancelled token) → independent cleanup
  token, media deleted in `finally`.
- A committed package could end as a failed job without `PackageId` → the job's link and success are
  committed in the package's own transaction; later finalization cannot undo it.
- U+001F could be injected into stored names → refused in files; exactly one separator decodes.
- Format docs contradictions (version list, `null`, `.qhub` game type).
Also found while checking: DOCX image deduplication (phase 1) means one stored file can serve several
questions; the editor and the media endpoints deleted a replaced file outright → `MediaService.DeleteIfUnreferenced`
keeps a file another question still uses (the backlog's "shared media files" item, now fixed for `.qhub` too).
Codex's look at these fixes found one more, fixed: a commit that reports an error although PostgreSQL committed
(a connection lost before the acknowledgement) deleted the package's media and unlinked the job → the importer
checks through a fresh connection whether the package exists and then finishes as a success; when that cannot be
told it keeps the media (`ImportCommitOutcomeTests`, which fails without the fix).
Regression: the 280 real DOCX packages give identical parser output to the pre-branch code. Tests: 1536 unit
+ 124 integration.

