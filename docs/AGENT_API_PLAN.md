# Agent API (write access on behalf of a user) — Implementation Plan

Let an AI agent (Claude Code, Codex, any MCP client) edit packages **on behalf of a site user**, with the
user's own permissions, a revocable personal token, an all-or-nothing changeset model with dry-run
preview, and a full audit trail. Branch: `feature/agent-api`. This document is the source of truth:
design decisions, phase list and a live checklist. Update it at the end of every phase.

Motivating use case: an editor emails a list of corrections to a freshly uploaded package; the admin
delegates the work to an agent with a token limited to that one package for a few days.

## Decision Log

| # | Decision | Choice |
|---|----------|--------|
| 1 | Auth model | **Personal access tokens (PAT)** bound to a user: `Authorization: Bearer qh_pat_<32 hex>`. SHA-256 hash stored, raw token shown once, never logged. Separate scheme from `X-API-Key`; agent endpoints authorize **only** the PAT scheme — cookies are never accepted (no CSRF surface). Identity's default cookie scheme is untouched |
| 2 | Token rights | Token acts **as its user** with the user's *current* state: token row, expiry, lockout and roles are read from the DB **on every request (no cache)**. Only Editor/Admin users can create/use tokens. Admin→Editor demotion immediately shrinks an admin token to own packages |
| 3 | Token shape | Name, scope (`Read` / `ReadWrite`), mandatory expiry (default 30 d, max 365 d), **package allowlist** (`int[]`; default in the UI: restricted — "all my packages" is an explicit choice), revocable; max 10 active tokens per user |
| 4 | Write model | **One write endpoint**: `POST /api/v1/manage/packages/{id}/changesets` — ordered operations applied **atomically**; `dryRun` returns the diff without saving; **`requestId` (required for apply)** makes retries idempotent; optional **`expectedVersion`** rejects stale previews |
| 5 | Engine | Load the package graph **tracked** (split query) in one `DbContext`, apply ops in memory, renumber in memory (`RenumberPackageInMemory`). Apply = one explicit transaction under `CreateExecutionStrategy`: save mutations → generated IDs resolved → write audit row → commit. Dry-run = same mutations, nothing saved, context discarded |
| 6 | Audit | Every applied changeset stored (`PackageChangesets`): user, token, request id + body hash, summary, ops JSON, diff JSON with **real IDs**, per-field before/after, relationship (author/editor/tag) before/after id sets, and **full snapshots of deleted entities**. Shown on the package manage page. Revert = future phase |
| 7 | Out of scope (v1) | Publish/unpublish/archive, `AccessLevel`, package create/delete (use `.qhub` import), block create/delete/split, **media upload/delete** (needs its own staging/atomicity design), **orphan author/tag deletion** (cross-package side effects), results/stats changes, refactoring `ManagePackageDetail.razor` onto the engine |
| 8 | Concurrency | Content **version** (SHA-256 fingerprint of the editable graph) returned by reads/dry-runs and checked against `expectedVersion` inside the apply transaction → 409 on mismatch (covers edits made in the UI too). In-process per-package lock + global engine concurrency limit (2). `TotalQuestions` recounted, never incremented. Stale-tab risk accepted (see Risks) |
| 9 | Rate limiting | **Two layers.** Pre-auth per-IP admission (middleware policies) + post-auth per-principal budgets keyed by **validated** token / API-client id (filter + singleton limiter, shared by REST and MCP). Fixes today's global-bucket bugs |
| 10 | Results safety | Structural operations are **rejected (422)** on packages that have any results source (results are mapped positionally to questions; structure changes would silently invalidate them). Field edits are allowed |
| 11 | MCP | Late phase: in-app MCP endpoint `/mcp` (official C# SDK, Streamable HTTP), same bearer token, tools mirror the REST API |
| 12 | URL namespace | `/api/v1/manage/...` (mirrors the site's `/manage/package/{id}`); separate nginx location |
| 13 | Testing | Unit tests (InMemory) for pure engine semantics; **PostgreSQL integration tests (Testcontainers, `postgres:16-alpine` + migrations)** for transactions, generated IDs, audit, idempotency, uniqueness races, cascades; **HTTP tests (`WebApplicationFactory`)** for auth isolation and rate limits |

### Rejected alternatives (and why)

- **Per-entity REST `PATCH`/`POST`/`DELETE` endpoints** — many round trips per correction list, no atomicity, no single preview, N audit rows per logical change. Can be added later on top of the same engine.
- **Reusing `X-API-Key` keys** — not bound to a user; existing keys are anonymous/read-only by design.
- **Calling `PackageManagementService` / `AuthorService.GetOrCreateAuthor` / `TagService.GetOrCreate` from the engine** — each creates its own context and/or calls `SaveChangesAsync`, which breaks atomicity and **would persist changes during a dry run**.
- **Partitioning rate limits by a hash of the raw credential** — attacker-controlled partition cardinality (every random token gets a fresh budget and a DB lookup). Replaced by per-IP admission + per-validated-principal budgets.
- **Single `SaveChanges` including the audit row** — new entities have no real IDs until saved, so the audit would record temporary IDs.
- **Caching token validation** — would let demoted/locked users keep their rights for the cache TTL; a per-request indexed lookup is cheap at this traffic.

---

## Rate Limiting (Phase 1)

### Problems today
- `api_general` / `api_detail` / `api_search` use `AddSlidingWindowLimiter` → **one global bucket shared by all API keys** (docs claim per key).
- `auth_limit` (5/min) is global → **the 6th login on the whole site within a minute gets 429**.
- Production publishes port `8080` on all interfaces and runs with `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (forwarded headers trusted from any source) → anyone reaching `:8080` directly can spoof `X-Forwarded-For`. Docker-published ports bypass UFW.

### Layer 1 — pre-auth admission per client IP (middleware policies, `RemoteIpAddress`)
| Policy | Endpoints | Limit per IP |
|---|---|---|
| `ip_public_api` | `/api/v1/packages*`, `/search`, `/editors`, `/tags/*` | 60/min |
| `ip_manage` | `/api/v1/manage/**`, `/mcp` | 150/min |
| `ip_auth` | `/api/Auth/*` | 5/min (was global) |

Bounds unauthenticated/garbage-credential traffic and token-lookup DB load before authentication runs.

### Layer 2 — post-auth budgets per validated principal (`ClientRateLimiter` singleton + `[ClientRateLimit("…")]` filter)
| Budget | Key | Limit |
|---|---|---|
| `app.general` / `app.detail` / `app.search` | `api_client_id` claim | 60 / 30 / 20 per min (existing numbers, now truly per key) |
| `agent.read` | `pat_id` claim | 120/min (all REST manage requests and every MCP request) |
| `agent.write` | `pat_id` claim | 20/min (changeset apply **and** dry-run, REST and MCP; enforced in the service so both paths share it) |

Sliding window, 6 segments, no queue; same JSON 429 body + `Retry-After`. Limits live in `appsettings.json` (`RateLimits` section) so they can be tuned without code changes. Web UI (Blazor/SignalR) has no app-level limits (unchanged). Humans never call `/api/v1/manage` (cookies rejected there), so "users vs agents vs apps" are structurally separate budgets.

### Proxy / forwarded headers
- Bind the production `web` service to `127.0.0.1:8080:8080` in `docker-compose.yml` (deployed by CD). nginx already proxies to `127.0.0.1:8080`.
- Keep `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (default `ForwardLimit = 1` takes the last `X-Forwarded-For` hop = nginx's `$remote_addr`, which `real_ip` resolved from `CF-Connecting-IP`). With loopback binding only the host's nginx can reach the app.

### nginx (manual deploy by the admin — `infra/nginx/` is not deployed by CD)
New zone `manage_zone` (per IP, 150r/m, burst 50) for `location /api/v1/manage/` and `location /mcp` (`client_max_body_size 1m`; `proxy_buffering off` + long read timeout for MCP streaming). `/api/v1/` and `/api/Auth/` unchanged.

---

## Data Model

### `PersonalAccessToken` (table `PersonalAccessTokens`, Phase 2)
| Column | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `UserId` | string | FK → `AspNetUsers`, cascade delete; index |
| `Name` | string(100) | e.g. "Claude — пакет 512" |
| `TokenHash` | string(64) | SHA-256 hex, **unique index** |
| `TokenPrefix` | string(16) | first 16 chars (`qh_pat_` + 9 hex), for display |
| `Scope` | enum `TokenScope { Read = 0, ReadWrite = 1 }` | |
| `PackageIds` | `int[]?` | null = all packages the user can edit; empty array is invalid (rejected at creation) |
| `CreatedAt`, `ExpiresAt` | timestamptz | `ExpiresAt` required |
| `LastUsedAt` | timestamptz? | updated at most once per minute |
| `RevokedAt` | timestamptz? | null = not revoked |

### `PackageChangeset` (table `PackageChangesets`, Phase 6)
| Column | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `PackageId` | int | FK → `Packages`, cascade delete; index `(PackageId, CreatedAt DESC)` |
| `UserId` | string? | FK → `AspNetUsers`, set null |
| `UserDisplayName` | string(200) | snapshot |
| `TokenId` | int? | FK → `PersonalAccessTokens`, set null |
| `TokenName` | string(100)? | snapshot |
| `RequestId` | Guid | **unique with `TokenId`** (idempotency) |
| `RequestHash` | string(64) | SHA-256 of the canonical request body; same `requestId` + different hash → 409 |
| `Summary` | string(500)? | agent-provided |
| `CreatedAt` | timestamptz | |
| `OperationCount` | int | |
| `VersionBefore`, `VersionAfter` | string(64) | content fingerprints |
| `OperationsJson` | jsonb | the request operations |
| `ChangesJson` | jsonb | diff with real IDs (the response `changes`, incl. deletion snapshots) |

---

## Authentication & Authorization (Phase 2)

- Scheme `PersonalAccessToken` (`PersonalAccessTokenAuthenticationHandler`): reads `Authorization: Bearer qh_pat_…`; anything else → `NoResult`. `PersonalAccessTokenService.Validate(raw)` does one query: token by hash, not revoked, not expired, user exists, not locked out, user roles ∋ Editor|Admin. No cache.
- Principal claims: `NameIdentifier` = user id, `Name` (display), role claims (`Editor`/`Admin` as currently assigned), `pat_id`, `pat_scope`, `pat_packages` (comma list; absent = unrestricted).
- Policies: `AgentRead` (`AuthenticationSchemes = PAT` only, authenticated, role Editor|Admin), `AgentWrite` (`AgentRead` + `pat_scope == ReadWrite`).
- `AgentAccess.CanEdit(principal, package)` = editing rights (`ClaimsPrincipal.CanAccessPackage`: admin, or editor-owner) **and** allowlist contains `package.Id` when present. **Not** `PackageAccessContext.GetAccessFilter()` (that is *viewing* rights).
- Error contract (all manage endpoints and MCP): missing/invalid token → 401 `{"error":"Missing or invalid token. Provide Authorization: Bearer qh_pat_…"}`; insufficient **scope** → 403; package not found / not editable / outside allowlist → **404** (never reveals existence).
- Token management UI actions are normal Blazor interactions under the cookie session (antiforgery unchanged). Raw tokens never appear in logs, URLs or audit rows.

---

## REST API (`/api/v1/manage`)

| Method & path | Policy / budget | Purpose |
|---|---|---|
| `GET /me` | AgentRead / agent.read | user, roles, token name/scope/expiry/allowlist |
| `GET /packages?status=&page=&pageSize=` | AgentRead / agent.read | packages the token can edit, newest first (`Id DESC`), max 50/page |
| `GET /packages/{id}` | AgentRead / agent.read | full editable tree + `version`: ids, `orderIndex`, numbers, all fields, authors/editors with ids, tags, status, numbering mode, shared editors, media URLs, `hasResults` |
| `GET /authors?search=` | AgentRead / agent.read | author lookup (≤20 results) |
| `GET /tags?search=` | AgentRead / agent.read | tag lookup (≤20 results) |
| `POST /packages/{id}/changesets` | AgentWrite / agent.write | apply or dry-run a changeset |
| `GET /packages/{id}/changesets` | AgentRead / agent.read | history list; `GET …/changesets/{changesetId}` detail |

### Changeset request
```jsonc
{
  "requestId": "6f1c…-uuid",            // required unless dryRun; client-generated, reuse on retry
  "expectedVersion": "a3f9…",           // optional: version from GET /packages/{id} or a dry-run; 409 if the package changed since
  "summary": "Виправлення за листом редактора від 08.10",   // optional, ≤500
  "dryRun": true,                        // default false
  "operations": [                        // 1..200
    { "op": "updateQuestion", "questionId": 501, "set": { "text": "…", "comment": null } },
    { "op": "setQuestionAuthors", "questionId": 501, "authors": [ { "id": 3 }, { "firstName": "Марія", "lastName": "Шевченко" } ] },
    { "op": "updateTour", "tourId": 101, "set": { "title": "…", "preamble": "…", "comment": "…" } },
    { "op": "setTourEditors", "tourId": 101, "authors": [ … ] },
    { "op": "updateBlock", "blockId": 7, "set": { "name": "…", "preamble": "…" } },
    { "op": "setBlockEditors", "blockId": 7, "authors": [ … ] },
    { "op": "updatePackage", "set": { "title": "…", "description": "…", "preamble": "…", "playedFrom": "2025-11-15", "playedTo": null } },
    { "op": "setPackageEditors", "authors": [ … ] },
    { "op": "setSharedEditors", "value": true },
    { "op": "setNumberingMode", "mode": "Manual" },
    { "op": "setTags", "tags": [ "2025", "Кубок" ] },
    // structural (Phase 7) — rejected on packages with results
    { "op": "addQuestion", "tourId": 101, "blockId": null, "position": 3, "set": { … }, "authors": [ … ] },
    { "op": "deleteQuestion", "questionId": 502 },
    { "op": "moveQuestion", "questionId": 503, "tourId": 102, "blockId": null, "position": 0 },
    { "op": "addTour", "position": 2, "type": "Regular", "set": { "title": "…", "preamble": "…", "comment": "…" }, "editors": [ … ],
      "questions": [ { "set": { … }, "authors": [ … ] } ] },
    { "op": "deleteTour", "tourId": 103 },
    { "op": "moveTour", "tourId": 104, "position": 1 },
    { "op": "setTourType", "tourId": 105, "type": "Shootout" }
  ]
}
```

### Operation semantics
- **`set` objects:** absent key = unchanged, `null` = clear (optional fields only; `text`/`answer` reject `null`, accept `""` with a warning). Unknown keys → 422.
- **Question fields:** `hostInstructions`, `text`, `handoutText`, `answer`, `acceptedAnswers`, `rejectedAnswers`, `answerForm`, `comment`, `source`, and `number` — **`number` only for ЩДК packages in `Manual` numbering mode** (the editor disables it otherwise) → 422 elsewhere. Своя гра pinned values (e.g. reserve ranges `"10-30"`) are preserved by renumbering as today.
- **Normalization identical to the editor:** `TextNormalizer.Normalize` for question text fields and tour title; `NormalizeExcludingApostrophes` for `source`; `Trim` for number, package title/description/preamble, tour preamble/comment, block name/preamble. Package `title` must stay non-empty.
- **Author refs:** `{ "id": n }` or `{ "firstName", "lastName" }` (trimmed, both non-empty; exact match reuses, else a new `Author` is **added to the changeset context** — never saved on dry-run). A changeset-local identity map dedups repeated names; a unique-index race with another writer → whole changeset fails with 409 (`retry`). ≤20 refs per op; duplicates removed.
- **Tags:** by name, trimmed, case-insensitive match via `ToLower()` equality (no `ILike` patterns), else added to the context; same identity map / 409 rule; ≤30 tags.
- **Cascades (identical to the editor):**
  - Своя гра `setTourEditors` on a theme that had **no** editors fills that theme's author-less questions (`CascadeThemeAuthorsToEmptyQuestions`).
  - Своя гра `setPackageEditors` (non-empty) fills author-less questions whose theme has no editors and whose block (if any) has no editors (`CascadePackageEditorsToEmptyQuestions`).
  - `setSharedEditors(true)` with no package editors copies the union of each tour's `AllEditors` (block editors for tours with blocks, tour editors otherwise).
  - `addQuestion` without `authors`: in a block → that block's editors only; otherwise tour editors, else package editors when `SharedEditors`, else none. Block-editor changes never cascade.
- **Structural invariants:** every referenced id belongs to this package and is not deleted earlier in the changeset; a `blockId` must belong to the target `tourId`; moving into a tour that has blocks requires `blockId`, into a tour without blocks forbids it; `position` is 0-based within the target container (tour root questions or a block), omitted = append, clamped to the end; entities are removed from/added to the in-memory collections (not only marked Deleted) so renumbering and counting see the real state. Своя гра: only `Regular` tours, no blocks; ЩДК: at most one warmup and one shootout (`setTourType` mirrors `OnTourTypeChanged` / `PackageRenumberingService.SetTourType`). `addTour` may include its `questions` (so a theme and its questions are one atomic op); later ops in the same changeset cannot reference entities created in it.
- **After ops:** renumber in memory if any structural op or `setNumberingMode` ran; `TotalQuestions` = surviving question count; validate; compute `versionAfter`.
- **Side effects after commit:** `PackageListService.InvalidateCache()` when title/editors/tags/shared-editors changed; `TagService.InvalidatePopularTagsCache()` when tags changed. **No orphan author/tag deletion** (deferred — the existing orphan check ignores package-level editor links). Deleted questions/tours keep their media files.
- **Limits:** request body ≤ 512 KB, 1–200 operations, ≤20 author refs/op, ≤30 tags, `addTour.questions` ≤ 30.

### Changeset response (200)
```jsonc
{
  "changesetId": 42,             // null on dry-run
  "dryRun": false,
  "versionBefore": "a3f9…", "versionAfter": "7c21…",
  "changes": [
    { "operationIndex": 0, "entity": "question", "id": 501, "label": "Тур 1, запитання 3", "field": "text", "before": "…", "after": "…" },
    { "operationIndex": 1, "entity": "question", "id": 501, "label": "…", "field": "authors",
      "before": [ { "id": 3, "name": "Марія Шевченко" } ], "after": [ { "id": 3, "name": "…" }, { "id": null, "name": "Нова Авторка" } ] },
    { "operationIndex": 11, "entity": "question", "id": 777, "label": "Тур 1, запитання 4", "kind": "added" },
    { "operationIndex": 12, "entity": "question", "id": 502, "label": "…", "kind": "deleted", "snapshot": { … all fields, authors … } },
    { "operationIndex": null, "entity": "question", "id": 503, "label": "…", "field": "number", "before": "5", "after": "6" }  // renumbering side effect
  ],
  "created": [ { "operationIndex": 11, "entity": "question", "id": 777 } ],  // ids are null on dry-run
  "warnings": [ "…" ],
  "totalQuestions": 36
}
```
Each op records its own entries (a field changed by two ops appears twice, with the op that changed it). Relationship values (authors, editors, tags) are `{ id, name }` objects; `id` is null for entities that do not exist yet (dry run). Renumbering side effects (`number`, `orderIndex`) have `operationIndex: null`. Generated search columns are never diffed. Replaying an applied `requestId` with the same body returns the stored result (200, `"replayed": true`); with a different body → 409.

Errors: 400 malformed JSON / limits exceeded / missing `requestId` on apply; 403 read-only token; 404 package not found/not editable/outside allowlist; 409 `expectedVersion` mismatch, `requestId` reuse with different body, uniqueness race; 422 `{ "error": "…", "operationIndex": 3 }` (validation; nothing saved); 429 rate limit; 503 package lock not acquired within 10 s.

---

## UI (Phases 3 and 9)

- **Profile** (`/Account/Profile`, Editor/Admin only): section «Токени доступу для агентів» — create (name, scope, expiry 7/30/90/365 days, package ids — default restricted, "усі мої пакети" is an explicit checkbox; for admins the warning says it means *all packages on the site*), raw token shown once with copy button, list (prefix, scope, packages, created, last used, expires, status), revoke.
- **Admin** (`/admin/api-keys`): extra section listing all users' tokens with revoke.
- **Package manage page**: collapsible «Історія змін (агенти)» — changesets newest first: date, user, token, summary, op count; per changeset a `<details>` diff (field: old → new). Toggling is client-side (`<details>`), no round trips.
- All UI text in Ukrainian.

---

## MCP (Phase 10)

`ModelContextProtocol.AspNetCore` mapped at `/mcp` (Streamable HTTP), `RequireAuthorization("AgentRead")`, `ip_manage` policy, `agent.read` budget via endpoint filter. Tools call the same services as REST: `whoami`, `list_packages`, `get_package`, `search_authors`, `search_tags`, `apply_changeset(operations, summary, dry_run, request_id, expected_version)` (checks `ReadWrite` scope + `agent.write`), `list_changesets`. Usage: `claude mcp add --transport http questions-hub https://questions.com.ua/mcp --header "Authorization: Bearer qh_pat_…"`.

---

## Phased Plan

Each phase: build → tests → **Codex review** (`gpt-6-astra`, effort `high`, read-only) of the phase diff → fix findings → `dotnet build` + `dotnet test` green → commit on `feature/agent-api` (never pushed) → update this checklist + Phase Notes.

- [x] **Phase 0 — Plan.** This doc + `AGENTS.md` (Codex context). Codex plan review applied (see notes).
- [x] **Phase 1 — Test infra + rate limiting.**
  - `QuestionsHub.IntegrationTests` project: Testcontainers Postgres fixture (migrations applied), `WebApplicationFactory<Program>` (`public partial class Program`), skips cleanly when Docker is unavailable. If the app's startup (media folders, background services) makes the factory impractical, fall back to filter/handler-level tests and record it here.
  - Layer 1 per-IP policies (`ip_public_api`, `ip_manage`, `ip_auth`) + Layer 2 `ClientRateLimiter` + `[ClientRateLimit]` filter; controllers switched to the new names in the same commit; `RateLimits` config section.
  - `docker-compose.yml`: production `web` → `127.0.0.1:8080:8080`. nginx `manage_zone` + locations. Fix rate-limit text in `docs/API.md` / `docs/AUTHENTICATION.md`.
  - Tests: per-key isolation (two keys don't share a bucket), per-IP auth limit, 429 body/`Retry-After`.
  - Separate small commit: fix `AuthorService.TryDeleteAuthorIfOrphaned` to also check `Author.Packages` (pre-existing bug) + test.
- [x] **Phase 2 — Tokens backend.** Entity + migration, `PersonalAccessTokenService` (create/validate/revoke/list, 10-active cap, expiry bounds, allowlist validation, throttled `LastUsedAt`), auth handler, `AgentRead`/`AgentWrite`, `AgentAccess.CanEdit`, `GET /api/v1/manage/me`. Tests: service (expired, revoked, locked, demoted Admin→Editor, Editor→User), HTTP: cookie-only → 401, cookie + invalid PAT → 401, admin cookie + restricted PAT → restricted, read scope vs write policy → 403.
- [x] **Phase 3 — Tokens UI.** Profile section + admin section.
- [x] **Phase 4 — Manage read endpoints.** `GET /packages`, `GET /packages/{id}` (incl. `version`, `hasResults`), `GET /authors`, `GET /tags` + DTOs, `PackageVersion` fingerprint. Tests: owner/admin/allowlist (404s), null vs restricted allowlist, DTO mapping incl. blocks and drafts, fingerprint stability and sensitivity.
- [x] **Phase 5 — Engine core (in-memory semantics).** Graph loader (split query, tracked), reference resolution with identity map, field/relationship ops (`update*`, `set*Authors|Editors`, `setTags`, `setSharedEditors`, `setNumberingMode` incl. renumber), cascades, normalization, diff builder (scalar + relationship sets), warnings, dry-run. Unit tests (InMemory) for every op, null/absent semantics, number-edit rule, cascades vs editor behavior, dry-run adds nothing to the DB.
- [x] **Phase 6 — Transactional apply + audit.** `PackageChangeset` entity + migration, transaction under execution strategy (mutations → IDs → audit → commit), `requestId` idempotency + body hash, `expectedVersion` check inside the transaction, per-package lock + global concurrency limit, `TotalQuestions` recount, cache invalidation. **Postgres integration tests:** atomic rollback on failing op, real IDs in audit, replay/409, version conflict, author unique race → 409.
- [x] **Phase 7 — Structural ops.** `addQuestion`/`deleteQuestion`/`moveQuestion`/`addTour` (+nested questions)/`deleteTour`/`moveTour`/`setTourType`, invariants, results guard, renumber side-effect diffs, deletion snapshots. Unit + Postgres tests incl. Своя гра, blocks, special tours.
- [x] **Phase 8 — Changeset REST endpoints.** `POST`/`GET` changesets, error mapping, body/ops limits, `agent.write` budget, `docs/API.md` agent section. HTTP tests for the contract.
- [x] **Phase 9 — History UI** on the package manage page.
- [x] **Phase 10 — MCP endpoint.**
- [x] **Phase 11 — End-to-end verification & docs.** Local run: create token in UI, `me`, read, dry-run, apply, history visible, replay, revoke → 401; docs (`API.md`, `AUTHENTICATION.md`, `CLAUDE.md` docs table), deferred items list.

### Future (not in this branch)
Media upload/delete via API; revert a changeset; orphan author/tag cleanup (after fixing the orphan check for all relationships); concurrency token on `Question` for the editor's blur-save; consolidating `ManagePackageDetail.razor` onto the engine; per-entity REST endpoints.

## Risks & Notes

- **Stale editor tab (accepted).** `ManagePackageDetail.razor` keeps the package in memory; the question editor saves *all* question fields on blur, and its structural handlers adjust `TotalQuestions` and order relative to stale state. If a human edits the same package while an agent applies a changeset, the UI can write stale values back or skew counters. Mitigations in v1: `expectedVersion` stops an agent from applying over edits it has not seen; `TotalQuestions` is recounted on every changeset; the audit log keeps before/after values for manual recovery; guidance "close the editor while an agent works". Possible later fix: concurrency token (`xmin`) checked by the editor's saves.
- **Logic duplication.** The editor's save rules are re-implemented in the engine (normalization, cascades, counters). Tests pin the engine; consolidating the razor page onto the engine is a follow-up.
- **Admin token blast radius.** An unrestricted admin token can edit every package; the UI defaults to a package allowlist and short expiry.
- **InMemory provider limits.** No relational transactions, no generated IDs semantics, no unique-index races, no jsonb/`int[]` SQL, and `InMemoryDbContextFactory` ignores transaction warnings — hence the Postgres integration tests for Phases 6–8. Manual transactions need `CreateExecutionStrategy` under `EnableRetryOnFailure`.
- **Memory.** Container limit is 512 MB. Graph loads use split queries; engine work is limited to 2 concurrent executions; request sizes are bounded.
- **Pre-existing bug found in review:** `AuthorService.TryDeleteAuthorIfOrphaned` ignores package-level editor links — the editor can delete an author who is still a package editor elsewhere (cascade removes them from that package). Fixed in Phase 1.

## Codex collaboration

- Codex runs via the Codex Claude Code plugin: `node <plugin>/scripts/codex-companion.mjs task --model gpt-6-astra --effort high --prompt-file <file>` (read-only unless `--write`).
- Context for Codex: `AGENTS.md` (repo root) → `CLAUDE.md` conventions → this plan. Each review prompt names the phase, the files touched and the acceptance criteria.
- Claude implements and owns this doc; Codex reviews the plan and each phase diff. Findings are triaged (real bugs fixed, disagreements recorded in Phase Notes).

## Phase Notes

### Phase 0 — Codex plan review (2026-10-08)
17 findings; adopted: no orphan cleanup in changesets (+ fix the existing orphan check), loopback port binding instead of new forwarded-headers config, two-layer rate limiting (pre-auth IP + validated principal), transactional audit with real IDs, required `requestId` idempotency, content-version `expectedVersion` + `TotalQuestions` recount, no token-validation cache, split-query loads + concurrency/body limits, identity map + 409 on uniqueness races + no `ILike` patterns, explicit structural invariants, `number` only in Manual mode, editor-inheritance rules matched to the actual UI handlers, nested questions in `addTour`, media deferred, structural ops blocked on packages with results, Postgres + HTTP integration tests, split engine phases, consistent 404/403 contract.

### Phase 1 — Test infra + rate limiting (2026-10-08)
- Done as planned. `WebApplicationFactory` turned out practical: Development environment + a pre-created `../uploads/handouts`, connection string via `UseSetting` (read during service registration), forwarded headers via a test `IStartupFilter` mirroring `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, and a guard that refuses to run unless the app uses the container DB. The container runs `db/scripts/01-extensions.sql` + `03-fts-setup.sql` with the hunspell dictionaries, like the `db-setup` service.
- Layer 2 is a middleware after `UseAuthorization` (not an MVC filter), reading `[ClientRateLimit]` endpoint metadata — `AuthorizationMiddleware` has already replaced `HttpContext.User` with the endpoint-scheme principal, and the same mechanism will cover the MCP endpoint. Action-level metadata overrides controller-level.
- Sliding-window leases carry no retry-after metadata; `Retry-After` falls back to 60 s (as before).
- Codex review: no production bugs. Fixed: fixture only skips on `DockerUnavailableException` and never when `CI=true`; no process-wide environment variables; `Invoke` instead of `InvokeAsync`; lease doc comment.
- Separate commit `bb84c82`: orphan-author check now includes package-level editors.
- **Manual deploy needed:** `infra/nginx/questions.com.ua.conf` (new `manage_zone`, `/api/v1/manage/`, `/mcp`). `docker-compose.yml` loopback binding ships with CD.

### Phase 2 — Tokens backend (2026-10-08)
- As planned, plus: validation also requires a **confirmed email** (password login does), creation and validation both check it; undefined `TokenScope` values are rejected; token creation is serialized by a process-wide lock so concurrent requests cannot exceed the 10-active cap (single-instance app; creation is rare).
- `LastUsedAt` stays a tracked, throttled update; concurrent validations may each write (harmless, milliseconds) — an atomic conditional update would need `ExecuteUpdate`, which the InMemory provider lacks.
- The write policy is tested over HTTP through a test-only `AgentPolicyProbeController` loaded as an application part of the test host (read token → 403 JSON, read-write → 200, admin cookie alone → 401). Cookie tests log in through the real form and prove the cookie authenticates cookie endpoints.
- Codex review: 2 medium (email confirmation, cap race), 2 low (scope enum, `LastUsedAt` race — accepted), 1 test gap (403) — all handled as above.

### Phase 3 — Tokens UI (2026-10-08)
- `Components/Account/AgentTokens.razor` on the profile (editors/admins), `Components/Pages/Admin/AgentTokensAdmin.razor` on `/admin/api-keys`. Copy-to-clipboard is a plain `onclick` (client-side). Package ids parsed by `PackageIdList` (unit-tested).
- Codex review (1 high, 2 medium, 2 low) — fixed: admin listing/revocation re-check the caller's **current** Admin role in the service (`GetAll(requestingUserId)`, `Revoke(tokenId, requestingUserId)` — no `isAdmin` flag from the UI), re-entrancy guards on handlers, separate error handling that keeps a just-created secret on screen, expiry state evaluated at render time, scope-aware admin warning, "Створено" column. Times use the site's existing convention (`ToLocalTime()`, server time zone) — a site-wide time-zone change is out of scope.
- Interactive Blazor flows could not be driven in a browser here (the Chrome extension blocks localhost by policy). Covered instead: service unit tests + HTTP tests of the prerendered pages (who sees what, own tokens only, admin overview, editors redirected from the admin page, names HTML-encoded, no secrets/hashes in the page). No bUnit added.

### Phase 4 — Manage read endpoints (2026-10-08)
- `PackageGraph` (shared include set `WithEditableGraph`, `Fingerprint`), `AgentApiNames` (camelCase enum names both ways — `/me` scope is now `readWrite`), `ManagePackagesController` + DTOs.
- Version = SHA-256 of a canonical JSON of everything the API edits (ids, order, numbers, texts, media URLs, author/editor/tag ids; collections sorted). Status, access level, publication date and `TotalQuestions` are excluded (not edited by the API).
- Codex review (3 medium) — fixed: the detail read runs in a **Repeatable Read** transaction under the execution strategy, so the split statements and the results check see one snapshot (the engine must load the same way); the detail query filters by editing rights before loading the graph (no work or timing signal for inaccessible packages); paging offset computed as `long`, pages past the end return empty. Test gaps closed (author/editor/tag/block permutations in the fingerprint test; mapping of package editors/tags/block editors/media URLs/`hasResults`; ownerless packages admin-only; restricted editor token; paging edges; tracked-graph fingerprint == read API version). A test that interleaves a concurrent edit between split statements was not added (needs statement-level interception); the snapshot guarantee comes from PostgreSQL.

### Phase 5 — Engine core (2026-10-08)
- `Infrastructure/AgentApi/Changesets/`: `ChangesetParser` (typed operations; case-insensitive names; unknown/duplicate properties rejected with the operation index), `ChangesetEngine` (field and relationship operations on a tracked graph; never saves), `ChangeRecord`/`ChangeFormatter` (diff with entity references; ids read at formatting time; entities still `Added` → id null).
- Deliberate deviations from the editor: whitespace-only optional text → null; `number` only in ЩДК Manual mode (empty allowed, like the editor); tour `title` only for Своя гра; `answerForm` only for Своя гра and `hostInstructions` only for ЩДК (clearing with null always allowed); lengths validated against the EF model.
- Codex review (2 high, 3 medium, 2 low) — fixed: shared-editors copy deduplicates by **instance** (unsaved authors all have id 0 under Npgsql; InMemory had masked it — Postgres regression test added); renumbering snapshots right before it runs and records every number/`orderIndex` it changes, including numbers set earlier in the changeset; side effects no longer depend on the primary value changing (package-editor cascade, shared-editors copy, renumber on every `setNumberingMode`); duplicate author properties rejected; empty manual numbers allowed; empty required fields always warn. Postgres tests also cover generated ids after save and Cyrillic case-insensitive tag reuse.

### Phase 6 — Transactional apply + audit (2026-10-08)
- `PackageChangeset` entity + migration `AddPackageChangesets` (unique `(TokenId, RequestId)`, history index `(PackageId, CreatedAt DESC)`); `PackageChangesetService` (+ `WarningsJson`, `TotalQuestionsAfter` columns beyond the original table sketch, for faithful replays).
- Flow: write budget → canonical request hash → rights check → replay check → engine slot + package lock (64 stripes, 10 s) → execution strategy (cancellation-aware) { clear tracker; Repeatable Read tx; rights; replay; load graph; `expectedVersion`; engine; save; diff with real ids; audit row; save; commit }. Unique violation on the request index → replay (after a rights check); on author/tag names → 409.
- Codex review (1 high, 3 medium, 2 low) — fixed: replays and request-id conflicts are only returned after a fresh rights check (a demoted admin no longer gets stored diffs of others' packages); request hash sorts object properties recursively (array order kept); caches invalidated after every confirmed apply, replays included (an uncertain commit confirmed by a retry used to skip it); bounded striped locks instead of a growing dictionary; cancellation reaches retry backoff. Tests: deterministic author race (waits for the blocked insert in `pg_stat_activity`), order-agnostic concurrency assertion, full replay comparison, cross-package request-id reuse, replay after demotion, and an interceptor that fails the audit insert to prove the already-saved mutations roll back.
- **Not changed (by design):** a concurrent editor-UI write to *other rows* during a changeset's Repeatable Read transaction commits alongside it. The schedule is serializable as "changeset, then UI write" (the changeset read nothing the UI wrote afterwards), so `VersionAfter` is consistent for that order and the next agent request with it as `expectedVersion` correctly gets 409. Same-row conflicts raise `40001`, which the execution strategy retries — the retry re-reads and the `expectedVersion` check then catches the change. Recorded under "Stale editor tab".

### Phase 7 — Structural operations (2026-10-08)
- `addQuestion` / `deleteQuestion` / `moveQuestion` / `addTour` (inline `questions`) / `deleteTour` / `moveTour` / `setTourType` in the engine. Positions are container-local (a tour's questions outside blocks, or one block) and every sibling `orderIndex` an operation shifts is recorded under that operation; renumbering then normalizes tour-wide and records the rest as side effects. Moves record a `location` field (`{tourId, blockId, position}` before/after). New entities are reported as `kind: "added"` with a snapshot of their final state (ids once saved) and listed in `created`; they cannot be addressed by id later in the same changeset. Deleted entities carry a full snapshot (tour: blocks with editors, questions with authors). Placeholder number `"0"` like the editor (kept in Manual mode); Своя гра pinned values (e.g. `10-30`) survive. Structural operations are rejected when results are attached (checked in the changeset's snapshot).
- Codex review (1 high, 2 medium) — fixed: results loading and changesets now share `PackageWriteLocks` (per-package, process-wide; the changeset takes it before its transaction and holds it through commit, `PackageResultsService.LoadSource` fetches from the platform first and then reads the layout, maps and writes under the lock), so a structural changeset cannot interleave with stats being mapped onto the old layout; a question added and later removed with its tour is dropped from the diff and `created` (no phantom creation); deletion snapshots are frozen at deletion time but rendered after saving, so authors created in the same changeset get real ids in the audit. Also: earlier records of entities that end up deleted keep the label they had. Regression tests for each, plus Manual `"0"`, pinned Своя гра values, clamped positions, same-container `location`, block-to-block move before deleting the tour, parser boundaries.

### Phase 8 — Changeset REST endpoints (2026-10-08)
- `ManageChangesetsController`: `POST /packages/{id}/changesets` (status mapping 200/400/422/404/409/429/503, `Retry-After`), history list + detail (rights checked first; 404 outside them). `docs/API.md` has the full "Agent API" section (tokens, limits, endpoints, operations, response, history, errors, workflow).
- Codex review (6 medium + doc/test gaps) — fixed: oversized bodies on `/api/v1` return 413 JSON (`UseApiErrorResponses`, inside the exception handler, which would otherwise render a 500 page); model-binding failures on `/api/v1` return `{ error, details }` instead of ProblemDetails (this also aligns the public read API with its documented error shape); nginx answers its own 429/413 on the API locations with JSON and `Retry-After` (manual nginx deploy); the docs now state the idempotency boundary (same token + package + request id + body), the "close the editor while an agent works" guidance, the exact block-author prefill rule, field length limits, normalization per field, `setTourType` vs `addTour` special-tour behaviour, `moveTour` reporting, manual-numbering defaults, history/detail shapes and paging, 415, and the two causes of 503. Tests: binding-error bodies, 415, explicit `null` properties, history rights/paging/cross-package detail, POSTs counting against the read budget; the 413 middleware is unit-tested (TestServer does not enforce body limits).

### Phase 9 — History UI (2026-10-09)
- `Components/PackageChangesetHistory.razor` below the tours on `/manage/package/{id}` (the page itself restricts access to the owner-editor and admins): latest 20 changesets (date, user, token, summary, operation count) from a summary-only query; a changeset's diff loads when «Показати зміни» is clicked; added/deleted entities show a one-line description plus the full snapshot in a client-side `<details>` (a deleted tour lists its questions with all fields). Formatting lives in `ChangeDisplay` (pure, unit-tested): Ukrainian field and enum labels, authors/tags by name, 1-based positions, `location`, known engine warnings translated, Ukrainian plurals; tolerant of unexpected stored JSON. The panel repeats the "reload the editor after an agent worked" warning.
- Codex review (2 medium, 2 low + hardening) — fixed: no eager loading of diffs/operations on every editor load (summary query; diffs on demand); deleted tours/questions fully inspectable; enum values and warnings in Ukrainian; guarded `location` formatting; operation count and «останні 20». Not changed: times use the site's `ToLocalTime()` convention (Phase 3 decision). Tests: owner and admin see the panel, another editor does not, agent-supplied text is HTML-encoded (raw HTML checked).

### Phase 10 — MCP endpoint (2026-10-09)
- `ModelContextProtocol.AspNetCore` 2.2.0: `AddMcpServer().WithHttpTransport(Stateless = true).WithTools<AgentMcpTools>()`, `MapAgentMcp()` → `/mcp` with `AgentRead` (token scheme only), `ip_manage` and the `agent.read` budget (one JSON-RPC message per POST, so per call). Eight tools (`whoami`, `list_packages`, `get_package`, `search_authors`, `search_tags`, `apply_changeset`, `list_changesets`, `get_changeset`) calling the same services as REST; the read side moved into `AgentPackageReader` (the REST controller is now thin). `apply_changeset` previews by default (`dryRun` defaults to true), checks the readWrite scope itself, and maps service failures to tool errors.
- Codex review (1 medium, 3 low) — fixed: `/mcp` bodies capped at 512 KB like the REST changeset endpoint (Kestrel limit set per request by `UseApiErrorResponses`, 413 JSON); structured content on all tools (`whoami` returns the same `AgentMeResponse` as `/me`); `apply_changeset` marked idempotent (preview is side-effect-free, applies replay by `requestId`); tests for revocation between calls and the read budget shared by REST and MCP. Docs: MCP subsection in `docs/API.md` (connecting Claude Code, tool table).

### Phase 11 — End-to-end verification (2026-10-09)
- Ran the real app (Kestrel, Development, dev PostgreSQL; the `AddPackageChangesets` migration applied on startup) with a token restricted to one draft Своя гра package (inserted directly: the token UI is interactive and the Chrome extension cannot open localhost here). Verified over HTTP: `/me`; list shows only the allowlisted package; another package → 404; full tree with version; dry run; apply; retry with the same `requestId` → `replayed`; stale `expectedVersion` → 409; history; invalid operation → 422 with index; structural dry run (deletion + renumber side effects); MCP `initialize` + `tools/call whoami` over Streamable HTTP; oversized bodies on `/mcp` and the REST changeset endpoint → JSON 413 from real Kestrel; the edit reverted through the API; profile page lists the token; manage page shows both changesets in the history; after revocation → 401.
- Found and fixed: MCP tool results escaped Cyrillic as `\uXXXX` (several times the tokens for Ukrainian text); the MCP JSON options now keep Unicode as-is (test added).

### Final whole-branch Codex review (2026-10-09)
Verdict "merge after fixes" — 2 must-fix (medium) + 2 follow-ups, all fixed: the history panel re-checks the viewer's **current database** roles/ownership (`PackageChangesetService.UserCanEdit`) when it loads and on every expansion (a session cookie outlives a demotion); tag names are now unique **case-insensitively** in the database (migration `TagsCaseInsensitiveUnique`: merges existing case-variant duplicates into the oldest tag, then a unique index on `lower("Name")` — the old `IX_Tags_Name_CI` with the deterministic `und-x-icu` collation never was), so concurrent `setTags` with different casing yields 409 instead of two tags; full-package reads (REST + MCP) are bounded to 4 at once (503 / tool error when busy); deleted-tour snapshots in the history UI show the tour's own fields and blocks. Tests for each (Postgres race, demotion, unit).

## Deployment checklist

1. Push `feature/agent-api`, squash-merge to `main` → CD deploys the image; EF migrations `AddPersonalAccessTokens`, `AddPackageChangesets` and `TagsCaseInsensitiveUnique` (merges tags that differ only by case — check the result) apply on startup; `docker-compose.yml` now binds the web port to `127.0.0.1:8080` (deployed by CD with the compose file).
2. **Manually** deploy `infra/nginx/questions.com.ua.conf` on the VPS (`manage_zone`, `/api/v1/manage/`, `/mcp`, JSON 429/413 pages) and reload nginx (`nginx -t && systemctl reload nginx`).
3. Smoke test: create a token on `/Account/Profile` restricted to one package, `GET /api/v1/manage/me`, a dry-run changeset, then revoke the token.
4. To delegate a package edit: create a `readWrite` token limited to that package with a short expiry; give it to the agent (REST: `Authorization: Bearer …`; Claude Code: `claude mcp add --transport http questions-hub https://questions.com.ua/mcp --header "Authorization: Bearer …"`); close the package editor while the agent works; review the history panel; revoke the token.

## Deferred / follow-ups

- Revert a changeset (the audit already stores full before/after values and deletion snapshots).
- Media upload/delete through the API (needs its own staging/atomicity design).
- Orphan author/tag cleanup after changesets (the orphan check now covers all relationships).
- Optimistic concurrency for the editor's own saves (e.g. `xmin` on `Question`) to close the stale-tab gap.
- Moving `ManagePackageDetail.razor` onto the changeset engine (one implementation of the save rules).
- Site-wide time zone for displayed times (currently server-local `ToLocalTime()` everywhere).
- Per-entity REST endpoints if a UI client ever needs them.
- Package export and import through the agent API — see the next section.

## Next feature: package export and import through the agent API (separate branch)

Recorded 2026-10-10, not started; build it with the `/feature` flow on its own branch.

**Goals.**
1. **Export for backups.** Download every package as a `.qhub`, so a scheduled job on Ivan's machine can keep a local, format-level archive of the whole site. This complements the server backups (`docs/BACKUPS.md`: restic database dumps and uploads); it does not replace them.
2. **Import through MCP.** A user connects their agent (Claude Code, Codex) to `/mcp`. The agent reads the `.qhub` specification, converts a local DOCX into a `.qhub` on the user's machine (the way `docs/QHUB_CONVERSION.md` does offline) and imports it into the site as a Draft owned by the user.

**What exists today.**
- Agents can only edit existing packages, through changesets.
- `get_package` returns the full JSON tree, without media and not as a `.qhub`.
- `.qhub` export exists only at `GET /api/packages/{id}/export`, which needs a browser cookie (Editor/Admin) and uses `QhubExporter`.
- Import exists only in the UI: `PackageImportService.Enqueue` creates a background job, which `QhubExtractor`/`DocxExtractor` and then `PackageDbImporter` process.

**Design sketch.**
- **Export.**
  - REST endpoint `GET /api/v1/manage/packages/{id}/export` that streams the `.qhub`, with AgentRead, the token's rights and allowlist, and `QhubExporter` reused.
  - The backup job uses an admin `read` token (all packages, up to 365 days): it calls `GET /api/v1/manage/packages` (paged) and then exports each package.
  - Incremental runs need a package-level "last modified" timestamp. `Package` has none today: add it, and have editor saves, changesets and results loading bump it. The list would then expose it, plus an `updatedSince` filter.
  - Exports read media from disk, so they get their own per-token budget, `agent.export` (for example 30/min).
  - No binary MCP tool: the backup job is a script (curl/PowerShell) and talks REST.
- **Spec for agents.**
  - Serve `docs/PACKAGE_FORMAT.md`, a JSON Schema of `package.json` and the conversion conventions from `docs/QHUB_CONVERSION.md`, both as MCP resources and as a `get_qhub_spec` tool (some clients do not surface resources).
- **Validation.**
  - A `validate_package` tool takes the `package.json` text (≤ 512 KB) and runs the same parsing and validation as `QhubExtractor`.
  - It returns errors, warnings and a summary (themes/tours, question count, authors) and never creates anything, so the agent can iterate until the conversion is clean.
- **Upload.**
  - `POST /api/v1/manage/imports` takes a multipart `.qhub` (≤ 50 MB; nginx needs `client_max_body_size` for that location). It enqueues the existing import job and returns its `jobId`.
  - `GET /api/v1/manage/imports/{jobId}` returns the status, warnings and the resulting `packageId`.
  - MCP tool `import_package` handles text-only packages (`package.json` inline, no assets): JSON-RPC bodies are capped at 512 KB. Packages with images go through the REST upload; an agent with a shell can use curl.
  - Decide whether this endpoint also accepts DOCX and uses the server's parser, or only `.qhub` (agent-side conversion copes better with unusual layouts).
- **Rights.**
  - Creating packages becomes a new token permission (`canImport`); the package allowlist cannot name packages that do not exist yet. Import is for Editors/Admins only, as in the UI.
  - The imported package is a Draft owned by the token's user. Decide whether a package-restricted token gets the new package added to its allowlist, so the agent can fix it up with changesets.
- **Limits and audit.**
  - Per-token budget `agent.import` (for example 10/hour), the same 50 MB cap, and the job queue's existing concurrency.
  - The job records `TokenId`. The import appears in the user's import list in the UI.
- **Security prerequisite.** Before exposing the import pipeline to tokens, re-run a security review of it (asset handling, file names, archive limits). Open findings are tracked privately until they are fixed.
- **Out of scope.** Updating an existing package from a `.qhub` (round-trip); media upload for existing packages (already deferred above).
