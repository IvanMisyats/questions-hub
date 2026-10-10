# Public API Reference

Two APIs share the `/api/v1` prefix:

- **Public read API** (this section up to "Agent API") — read-only, for external clients (mobile apps, integrations), authenticated with an `X-API-Key`.
- **[Agent API](#agent-api)** (`/api/v1/manage`) — lets an AI agent read and edit packages **on behalf of a site user**, authenticated with that user's personal access token.

**Base URL**: `https://questions.com.ua/api/v1`

---

## Authentication

All requests require an API key via the `X-API-Key` header:

```
GET /api/v1/packages HTTP/1.1
Host: questions.com.ua
X-API-Key: qh_live_a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
```

Keys are created by the site admin at `/admin/api-keys`. Each key is shown **once** at creation — store it securely.

### Key format

`qh_live_<32 hex chars>` (40 characters total).

---

## Rate Limits

| Layer | Scope | Limit |
|-------|-------|-------|
| Nginx | Per IP | 30 req/min on `/api/v1/` (burst 10) |
| ASP.NET, before authentication | Per IP | 60 req/min on all public API endpoints |
| ASP.NET, after authentication | Per API key | 60 req/min (general endpoints) |
| ASP.NET, after authentication | Per API key | 30 req/min (`/packages/{id}`) |
| ASP.NET, after authentication | Per API key | 20 req/min (`/search`) |

Each key has its own budget; the per-IP layer bounds requests with missing or invalid keys. All ASP.NET limits are sliding one-minute windows, configurable in the `RateLimits` section of `appsettings.json`.

When exceeded, the API returns `429 Too Many Requests` with a `Retry-After` header (seconds).

---

## Access Rules

Only **published** packages with access level **All** (public) are visible through the API. Draft, archived, and restricted-access packages are not returned.

---

## Endpoints

### `GET /api/v1/packages`

Browse and filter packages with pagination.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `search` | string | — | Title search (case-insensitive, partial match) |
| `editor` | int | — | Filter by editor ID |
| `tag` | int | — | Filter by tag ID |
| `type` | string | — | Game-type filter: `www` (Що?Де?Коли?) or `shvager` (Своя гра). Omit for both. Invalid values → `400` |
| `sort` | string | `publicationDate` | Sort field: `publicationDate` or `playedFrom` |
| `dir` | string | `desc` | Sort direction: `asc` or `desc` |
| `page` | int | 1 | Page number (1-based) |
| `pageSize` | int | 20 | Results per page (1–50) |

**Response:**

```json
{
  "packages": [
    {
      "id": 42,
      "title": "Кубок Львова 2025",
      "type": 0,
      "description": "Опис пакету...",
      "publicationDate": "2025-12-15T10:30:00Z",
      "playedFrom": "2025-12-01",
      "playedTo": "2025-12-02",
      "toursCount": 6,
      "questionsCount": 72,
      "editors": [
        { "id": 1, "firstName": "Іван", "lastName": "Петренко" }
      ],
      "tags": [
        { "id": 5, "name": "2025" }
      ],
      "hasResults": true
    }
  ],
  "totalCount": 150,
  "totalPages": 8,
  "currentPage": 1
}
```

`type` in list items is numeric: `0` = Що?Де?Коли?, `1` = Своя гра. `toursCount` is the number of tours (ЩДК) or themes (Своя гра). `hasResults` is `true` when the package has at least one loaded tournament-results source.

---

### `GET /api/v1/packages/{id}`

Full package detail with tours, blocks, and questions.

Returns `404` if the package does not exist or is not public.

**Response:**

```json
{
  "id": 42,
  "title": "Кубок Львова 2025",
  "gameType": "www",
  "description": "Опис пакету...",
  "preamble": "Редактори дякують тестерам...",
  "playedFrom": "2025-12-01",
  "playedTo": "2025-12-02",
  "publicationDate": "2025-12-15T10:30:00Z",
  "questionsCount": 72,
  "numberingMode": "global",
  "editors": [
    { "id": 1, "firstName": "Іван", "lastName": "Петренко" }
  ],
  "tags": [
    { "id": 5, "name": "2025" }
  ],
  "isAdult": false,
  "tours": [
    {
      "id": 101,
      "number": "1",
      "title": null,
      "type": "regular",
      "preamble": null,
      "comment": null,
      "editors": [
        { "id": 2, "firstName": "Олена", "lastName": "Коваленко" }
      ],
      "blocks": [],
      "questions": [
        {
          "id": 501,
          "number": "1",
          "hostInstructions": "Перед запитанням роздайте аркуші",
          "text": "Текст запитання...",
          "answer": "Відповідь",
          "handoutText": "Текст роздатки",
          "handoutUrl": "https://questions.com.ua/media/handout_q501.jpg",
          "acceptedAnswers": "Залік",
          "rejectedAnswers": null,
          "answerForm": null,
          "comment": "Коментар з поясненням",
          "commentAttachmentUrl": null,
          "source": "Вікіпедія",
          "authors": [
            { "id": 3, "firstName": "Марія", "lastName": "Шевченко" }
          ]
        }
      ]
    }
  ]
}
```

#### Field notes

| Field | Values | Description |
|-------|--------|-------------|
| `gameType` | `www`, `shvager` | Game type: Що?Де?Коли? or Своя гра |
| `numberingMode` | `global`, `perTour`, `manual` | How question numbers are assigned (ЩДК only; ignore for Своя гра) |
| `tours[].title` | string or `null` | Theme title (Своя гра). `null` for ЩДК tours |
| `tours[].type` | `regular`, `warmup`, `shootout` | Tour type. Warmup is always first, shootout always last. Always `regular` for Своя гра |
| `questions[].number` | string | Question number (ЩДК) or value `"10"`–`"50"` (Своя гра) |
| `questions[].answerForm` | string or `null` | «Форма» hint (Своя гра). `null` for ЩДК |
| `isAdult` | boolean | `true` if package is tagged "18+" |
| `handoutUrl`, `commentAttachmentUrl` | absolute URL or `null` | Media files (images, video, audio) |
| `hostInstructions` | string or `null` | Instructions for the game host |

#### Structure

- **Tours** are ordered by `orderIndex` (warmup first, shootout last, regular tours in between)
- **Blocks** are optional sub-divisions within a tour. When present, questions belong to blocks
- **Questions** at the tour level (`questions` array) are questions **not** inside any block
- Questions inside blocks appear in `blocks[].questions`

---

### `GET /api/v1/search`

Full-text search across questions from published public packages.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `q` | string | **required** | Search query |
| `limit` | int | 50 | Max results (1–100) |
| `type` | string | — | Game-type filter: `www` (Що?Де?Коли?) or `shvager` (Своя гра). Omit for both. Invalid values → `400` |

**Query syntax:**

| Syntax | Example | Meaning |
|--------|---------|---------|
| `word1 word2` | `сепульки антарктида` | AND — both words required |
| `word1 OR word2` | `кіт OR собака` | OR — either word |
| `"exact phrase"` | `"чорний кіт"` | Phrase — exact word sequence |
| `-word` | `тварина -кіт` | Exclude word |

Supports Ukrainian morphology (word forms), accent-insensitive matching, prefix search, and typo tolerance.

**Response:**

```json
{
  "query": "сепульки",
  "count": 2,
  "results": [
    {
      "questionId": 501,
      "tourId": 101,
      "packageId": 42,
      "packageTitle": "Кубок Львова 2025",
      "gameType": "www",
      "tourNumber": "1",
      "tourTitle": null,
      "questionNumber": "3",
      "text": "Текст запитання...",
      "answer": "Відповідь",
      "handoutText": null,
      "handoutUrl": null,
      "acceptedAnswers": null,
      "rejectedAnswers": null,
      "answerForm": null,
      "comment": "Коментар",
      "commentAttachmentUrl": null,
      "source": "Лем С. Зоряні щоденники",
      "textHighlighted": "Текст <mark>сепульки</mark>...",
      "answerHighlighted": "Відповідь",
      "handoutTextHighlighted": null,
      "acceptedAnswersHighlighted": null,
      "rejectedAnswersHighlighted": null,
      "commentHighlighted": "Коментар",
      "sourceHighlighted": "Лем С. Зоряні щоденники",
      "authors": [
        { "id": 3, "firstName": "Марія", "lastName": "Шевченко" }
      ],
      "isAdult": false,
      "rank": 1.85
    }
  ]
}
```

Highlighted fields contain `<mark>` tags around matched terms. Use these for rendering search result previews.

For Своя гра results (`"gameType": "shvager"`): `tourTitle` holds the theme name, `questionNumber` holds the value (`"10"`–`"50"`), and `answerForm` holds the «Форма» hint. These fields are `null` for Що?Де?Коли? results.

---

### `GET /api/v1/editors`

List editors of published packages. Use for filter dropdowns.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `search` | string | — | Filter by name (partial, case-insensitive) |

**Response:**

```json
{
  "editors": [
    { "id": 1, "fullName": "Іван Петренко" }
  ]
}
```

---

### `GET /api/v1/tags/popular`

Most popular tags across published packages.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `count` | int | 10 | Max results (1–50) |

**Response:**

```json
{
  "tags": [
    { "id": 5, "name": "2025" }
  ]
}
```

---

## Error Responses

All errors return JSON:

```json
{ "error": "Description of the error." }
```

| Status | Meaning |
|--------|---------|
| `400` | Bad request (e.g., missing required `q` parameter) |
| `401` | Missing or invalid API key |
| `404` | Package not found or not public |
| `429` | Rate limit exceeded (check `Retry-After` header) |

---

<!-- agent-reference:start — from here to agent-reference:end is served to agents by the MCP tool get_api_reference -->
## Agent API

Lets an agent (Claude Code, Codex, an MCP client, a script) edit packages **as a site user**: same rights as the user, narrowed by the token.

**Over MCP** (`/mcp`) every endpoint below is a tool (see the table in [MCP](#mcp)) taking the same fields as arguments. HTTP errors become tool errors whose text is the `error` message described here — e.g. `Operation 2: …` for an invalid operation (`422`), a stale `expectedVersion` for a conflict (`409`).

### Getting a token

Editors and admins create tokens on their profile page (`/Account/Profile` → «Токени доступу для агентів»): name, scope (`read` / `readWrite`), expiry (7, 30, 90 or 365 days) and — by default — the packages the token may touch. The token (`qh_pat_` + 32 hex) is shown **once**. Revoke it there (admins can revoke anyone's on `/admin/api-keys`).

```
GET /api/v1/manage/me HTTP/1.1
Host: questions.com.ua
Authorization: Bearer qh_pat_00112233445566778899aabbccddeeff
```

- The token acts with the user's **current** rights on every request: an admin token can edit any package, an editor token only the user's own packages; the token's package list narrows that further. Revocation, expiry, lockout and role changes apply to the next request.
- Session cookies and `X-API-Key` are **not** accepted on `/api/v1/manage`.
- Everything outside the token's rights — another user's package, a package not in the token's list, a missing package — is `404`.

### Rate limits

| Layer | Scope | Limit |
|-------|-------|-------|
| Nginx | Per IP | 150 req/min on `/api/v1/manage/` and `/mcp` (burst 50) |
| ASP.NET, before authentication | Per IP | 150 req/min |
| ASP.NET, after authentication | Per token | 120 requests/min (all manage requests) |
| Changeset service | Per token | 20 changesets/min (applies **and** dry runs) |

`429` responses carry `Retry-After` (seconds); all errors, including the proxy's own 429/413, are JSON.

### Endpoints

| Method & path | Scope | Purpose |
|---|---|---|
| `GET /api/v1/manage/me` | read | The user (id, name, roles) and the token (name, scope, expiry, package list) |
| `GET /api/v1/manage/packages?status=&page=&pageSize=` | read | Packages the token can edit (drafts included), newest first; `status` = `draft`/`published`/`archived`; `pageSize` ≤ 50 |
| `GET /api/v1/manage/packages/{id}` | read | The full editable tree and its `version` |
| `GET /api/v1/manage/authors?search=` | read | Authors whose first or last name starts with `search` (≤ 20) |
| `GET /api/v1/manage/tags?search=` | read | Matching tags (≤ 20) |
| `POST /api/v1/manage/packages/{id}/changesets` | readWrite | Apply or preview a changeset |
| `GET /api/v1/manage/packages/{id}/changesets?page=&pageSize=` | read | Applied changesets, newest first (see [History](#history)) |
| `GET /api/v1/manage/packages/{id}/changesets/{changesetId}` | read | One changeset: operations, diff, warnings |

Enum values are camelCase strings (`www`/`shvager`, `draft`, `global`/`perTour`/`manual`, `regular`/`warmup`/`shootout`, `read`/`readWrite`).

#### `GET /api/v1/manage/packages/{id}`

Like the public detail, plus everything needed to address changes: ids, `orderIndex`, `blockId`, `status`, `accessLevel`, `numberingMode`, `sharedEditors`, package `editors` and `tags` with ids, author ids, `hasResults`, and `version` — a SHA-256 fingerprint of the editable content. Questions inside blocks are listed under `tours[].blocks[].questions`; `tours[].questions` holds the questions outside blocks.

#### Package model

- **Game types.** «Що? Де? Коли?» (ЩДК, `gameType: "www"`): tours of numbered questions; one tour may be the warmup (`type: "warmup"`, розминка, always first) and one the shootout (`"shootout"`, перестрілка, always last). «Своя гра» (`"shvager"`): every tour is a theme — its `title` is the theme name and a question's `number` is its value, set by position (normally five questions: 10, 20, 30, 40, 50; a sixth would be 60). Non-numeric values such as a reserve question's `10-30` are kept as they are.
- **Structure.** Package → tours → optional blocks → questions. A tour either has blocks (then every question belongs to one) or none. `numberingMode` (ЩДК): `global` (numbers run through the regular tours and the shootout), `perTour` (restart at 1 in each tour) or `manual` (numbers are typed by hand and never recomputed). The warmup always restarts at 1 and does not advance the global count, so a package can have both a warmup question 1 and a regular question 1 — identify a question by its tour as well.
- **Finding what to change.** People name questions by tour and displayed number («тур 2, питання 17», «тема "Річки", 30»). Look the `id` up in the package tree — never guess ids.
- **Question fields** (label in the editor → field): Текст → `text`; Відповідь → `answer`; Залік → `acceptedAnswers`; Незалік → `rejectedAnswers`; Коментар → `comment`; Джерело → `source`; Автори → `authors`; Роздатка → `handoutText` (its picture: `handoutUrl`); Вказівка ведучому → `hostInstructions` (ЩДК); Форма → `answerForm` (Своя гра); Ілюстрація до коментаря → `commentAttachmentUrl`. Media URLs cannot be changed through the API.
- **People.** Question `authors`; tour (theme) and block `editors`; package `editors`. The package-level `editors` list is the one the site shows only when `sharedEditors` is `true`; when it is `false` the site shows the editors of the tours and blocks, so change those (`setTourEditors`, `setBlockEditors`) — the stored package list may be empty or outdated then.
- **Text.** Content is Ukrainian; keep the wording exactly as given (the API normalizes apostrophes, dashes and whitespace itself). Stress marks (a combining acute accent, U+0301, as in «о́піки») are part of the text: kept exactly as sent, never added or removed by the API.
- **Editing a field.** `set` replaces a field's whole value. To add to a field — another accepted answer, a sentence in a comment — send the current value with the addition. `acceptedAnswers` (Залік) and `rejectedAnswers` (Незалік) are free text: variants separated by `; `, ending with a period. «точна відповідь.» in Залік means that only the exact answer is accepted, so it no longer applies once another variant is accepted.
- **Своя гра and `numberingMode`.** The returned `numberingMode` does not control Своя гра numbering and `setNumberingMode` is rejected: a theme's values follow the positions of its questions, except non-numeric values (such as `10-30`), which stay as they are.

### Changesets

A changeset is an ordered list of operations applied **atomically**: all of them or none.

```http
POST /api/v1/manage/packages/512/changesets
Authorization: Bearer qh_pat_…
Content-Type: application/json

{
  "requestId": "6f1c4b6e-0d3a-4b8e-9d2f-1a2b3c4d5e6f",
  "expectedVersion": "a3f9…",
  "summary": "Виправлення за листом редактора від 08.10",
  "dryRun": false,
  "operations": [
    { "op": "updateQuestion", "questionId": 501, "set": { "text": "…", "comment": null } },
    { "op": "setQuestionAuthors", "questionId": 501, "authors": [ { "id": 3 }, { "firstName": "Марія", "lastName": "Шевченко" } ] }
  ]
}
```

| Field | Meaning |
|---|---|
| `requestId` | Client-generated UUID, **required to apply**. Retrying with the **same token, package, `requestId` and body** never applies the changeset twice: the stored result comes back with `"replayed": true`. The same `requestId` with a different body (or package) → `409`. Ids are scoped to the token: if you lost a response and must continue with a *different* token, re-read the package (or its history) to find out whether the changeset was applied — or always send `expectedVersion`, which makes a second application fail with `409`. |
| `expectedVersion` | Optional: `version` from `GET /packages/{id}`, or `versionBefore` of a dry run. If the package changed since — through the API or the editor — the changeset is rejected with `409`; re-read and rebuild. |
| `summary` | Optional description (≤ 500 chars), shown in the package history. |
| `dryRun` | `true` previews: the response shows exactly what would change, nothing is saved (no `requestId` needed). |
| `operations` | 1–200 operations, applied in order. Body ≤ 512 KB. |

**Recommended workflow:** `GET` the package → build operations → `dryRun: true` with `expectedVersion` → check the diff → apply with the same `expectedVersion` and a fresh `requestId` (retry with the same id on network errors).

> **Close the package editor while an agent works.** An editor tab opened before the agent's changes keeps the old content in memory and writes a question's fields back when you leave a field. `expectedVersion` protects the agent from overwriting newer edits, not a stale editor tab from overwriting the agent's. Reload the editor after an agent finished; every applied changeset is listed in the package history.

#### Operations

Common rules (violations → `422` with the operation index):

- `set` objects: a key that is absent leaves the field unchanged; `null` clears an optional field; at least one key; unknown or duplicate keys are rejected (names are case-insensitive).
- Author references (≤ 20 per array): `{ "id": n }` or `{ "firstName", "lastName" }` — both names required, trimmed; an existing author with exactly that name is reused, otherwise one is created. Duplicates are ignored.
- `authors`, `editors` and `tags` **replace the whole list**; `[]` clears it. To add one co-author or tag, send the current entries (ids from the package tree) plus the new one; to remove one, send the rest. Example — keep author 3 and add a new co-author: `{ "op": "setQuestionAuthors", "questionId": 501, "authors": [ { "id": 3 }, { "firstName": "Олена", "lastName": "Коваленко" } ] }`.
- Ids are positive integers. Unknown operation names and properties are rejected.
- Text: question fields and Своя гра theme titles are normalized like in the editor (apostrophes, dashes, special whitespace; `source` keeps apostrophes for URLs); package fields, tour preamble/comment and block fields are trimmed. Whitespace-only optional text is stored as `null`.
- Lengths: package `title` 500, `description` 2000; author names 100 each; tag 100; tour `title` 200, `comment` 2000; block `name` 200; question `number` 20; `answer`, `hostInstructions`, `acceptedAnswers`, `rejectedAnswers`, `answerForm` 1000 each.

| Operation | Fields | Notes |
|---|---|---|
| `updatePackage` | `set`: `title`, `description`, `preamble`, `playedFrom`, `playedTo` (`yyyy-MM-dd`) | `title` cannot be empty |
| `setPackageEditors` | `authors` | Своя гра: also fills author-less questions whose theme/block has no editors |
| `setSharedEditors` | `value` (bool) | Turning on with no package editors copies the editors in use |
| `setNumberingMode` | `mode`: `global` / `perTour` / `manual` | ЩДК only; renumbers |
| `setTags` | `tags`: names (≤ 30) | Case-insensitive reuse of existing tags |
| `updateTour` | `tourId`, `set`: `title` (Своя гра theme name only), `preamble`, `comment` | |
| `setTourEditors` | `tourId`, `authors` | Своя гра: a theme that had no editor fills its author-less questions |
| `updateBlock` | `blockId`, `set`: `name`, `preamble` | |
| `setBlockEditors` | `blockId`, `authors` | |
| `updateQuestion` | `questionId`, `set`: `text`, `answer`, `hostInstructions` (ЩДК), `handoutText`, `acceptedAnswers`, `rejectedAnswers`, `answerForm` (Своя гра), `comment`, `source`, `number` | `text`/`answer` cannot be `null` (use `""`, which warns). `number` only in ЩДК packages with `manual` numbering. Media cannot be changed through the API. |
| `setQuestionAuthors` | `questionId`, `authors` | |
| `addQuestion` | `tourId`, `blockId`?, `position`?, `set`?, `authors`? | `blockId` is required for (and must belong to) a tour with blocks, forbidden otherwise. Without `authors`: in a block → that block's editors (none if it has none); outside blocks → tour editors, else the package editors when shared. `authors: []` creates it without authors. |
| `deleteQuestion` | `questionId` | Media files are kept |
| `moveQuestion` | `questionId`, `tourId`, `blockId`?, `position`? | Across tours and blocks |
| `addTour` | `position`?, `type`?, `set`?, `editors`?, `questions`? (≤ 30 × `{ set?, authors? }`) | A new tour/theme with its questions in one operation. A second warmup/shootout is rejected (use `setTourType`). |
| `deleteTour` | `tourId` | Deletes its blocks and questions |
| `moveTour` | `tourId`, `position` (required) | Reported as `orderIndex` changes |
| `setTourType` | `tourId`, `type`: `regular` / `warmup` / `shootout` | At most one warmup and one shootout: the tour that had the type becomes regular. Своя гра themes are always regular. |

Positions are 0-based within the target container (a tour's questions outside blocks, or one block); omitted (where optional) = append; past the end = append. After structural operations questions and tours are renumbered (warmup first, shootout last; Своя гра values follow positions, ranges like `10-30` are kept). In `manual` numbering existing numbers are kept and a new question gets `"0"` unless `set.number` is given. Entities created by a changeset cannot be referenced by later operations of the same changeset — use `addTour.questions` to create a theme with its questions.

**Structural operations** (`addQuestion`, `deleteQuestion`, `moveQuestion`, `addTour`, `deleteTour`, `moveTour`, `setTourType`) are rejected when tournament results are attached to the package (`hasResults: true`): per-question statistics are mapped by position. Make such changes in the editor.

#### Response

```json
{
  "changesetId": 42,
  "dryRun": false,
  "replayed": false,
  "versionBefore": "a3f9…",
  "versionAfter": "7c21…",
  "changes": [
    { "operationIndex": 0, "entity": "question", "id": 501, "label": "Тур 1, запитання 3", "field": "text",
      "before": "…", "after": "…", "kind": null, "snapshot": null },
    { "operationIndex": 1, "entity": "question", "id": 501, "label": "Тур 1, запитання 3", "field": "authors",
      "before": [ { "id": 3, "name": "Марія Шевченко" } ],
      "after": [ { "id": 3, "name": "Марія Шевченко" }, { "id": 17, "name": "Нова Авторка" } ], "kind": null, "snapshot": null },
    { "operationIndex": 2, "entity": "question", "id": 777, "label": "Тур 1, запитання 4", "field": null,
      "before": null, "after": null, "kind": "added", "snapshot": { "id": 777, "text": "…", "authors": [ … ], … } },
    { "operationIndex": null, "entity": "question", "id": 503, "label": "Тур 1, запитання 5", "field": "number",
      "before": "4", "after": "5", "kind": null, "snapshot": null }
  ],
  "created": [ { "operationIndex": 2, "entity": "question", "id": 777 } ],
  "warnings": [],
  "totalQuestions": 37
}
```

Every property is always present (`null` when not applicable).

- Every change is listed with the operation that caused it; renumbering side effects have `operationIndex: null`. Question moves have `field: "location"` with `{ tourId, blockId, position }`; tour moves show as `orderIndex` changes.
- Added entities have `kind: "added"` and a `snapshot` of their final state; deleted ones `kind: "deleted"` and a full `snapshot` of what was removed. Ids of new entities are `null` in a dry run.
- `created` lists new questions and tours. Authors and tags that a changeset creates (referenced by a name that does not exist yet) are reported in `warnings` — `New author 'Ім'я Прізвище' will be created …` — so check their spelling in the preview.
- `versionAfter` is the package `version` after the changeset (`null` on a dry run; `changesetId` is `null` too) — use it as the next `expectedVersion`. For a dry run use `versionBefore`.

#### History

`GET /api/v1/manage/packages/{id}/changesets?page=1&pageSize=20` (page ≥ 1, pageSize 1–100; beyond the last page → empty list), newest first (`createdAt`, then `id`, descending):

```json
{
  "changesets": [
    { "id": 42, "createdAt": "2026-10-08T21:40:00Z", "userDisplayName": "Іван Петренко", "tokenName": "Claude — пакет 512",
      "summary": "Виправлення за листом редактора", "operationCount": 7, "versionBefore": "a3f9…", "versionAfter": "7c21…" }
  ],
  "totalCount": 1, "page": 1, "pageSize": 20
}
```

`GET /api/v1/manage/packages/{id}/changesets/{changesetId}` returns `{ "changeset": { …summary… }, "operations": [ …as sent… ], "changes": [ …as in the response… ], "warnings": [ … ], "totalQuestionsAfter": 37 }`. A changeset of another package, or a missing one, is `404`.

#### Errors

| Status | Meaning |
|---|---|
| `400` | Malformed JSON or a wrong type (e.g. `"dryRun": "yes"`, a `requestId` that is not a UUID) — `{ "error": "…", "details": { field: [messages] } }`; no or too many operations; missing `requestId` when applying; `summary` too long |
| `401` | Missing, invalid, expired or revoked token |
| `403` | The token's scope does not allow changes (`read`) |
| `404` | Package not found, or outside the token's rights |
| `409` | `expectedVersion` is stale; `requestId` reused for another request; a concurrent change created the same author/tag (retry) |
| `413` | Body over 512 KB |
| `415` | `Content-Type` is not `application/json` |
| `422` | An operation is invalid: `{ "error": "…", "operationIndex": 3 }` — nothing was saved |
| `429` | Rate limit (`Retry-After`) |
| `503` | Busy: another change is being applied to this package, or the server is processing the maximum number of changesets (or full package reads) at once (`Retry-After: 5`) |

All errors are JSON `{ "error": "…" }` (400 binding errors add `details`). Every applied changeset is recorded in the package history (who, which token, summary, the full diff), visible to the package's editors.

### MCP

The same API as MCP tools at `https://questions.com.ua/mcp` (Streamable HTTP, stateless), with the same token, rights, limits and rules. The server is self-describing: it sends usage instructions when a client connects, `apply_changeset` lists every operation with its fields, and `get_api_reference` (also the resource `questions-hub://docs/agent-api`) returns this whole "Agent API" section. Connect Claude Code with:

```bash
claude mcp add --transport http questions-hub https://questions.com.ua/mcp \
  --header "Authorization: Bearer qh_pat_…"
```

| Tool | REST equivalent |
|---|---|
| `whoami` | `GET /me` |
| `list_packages` (`status`?, `page`?, `pageSize`?) | `GET /packages` |
| `get_package` (`packageId`) | `GET /packages/{id}` |
| `search_authors` (`search`), `search_tags` (`search`) | `GET /authors`, `GET /tags` |
| `apply_changeset` (`packageId`, `operations`, `dryRun` = **true** by default, `requestId`?, `expectedVersion`?, `summary`?) | `POST /packages/{id}/changesets` |
| `list_changesets` (`packageId`, `page`?, `pageSize`?), `get_changeset` (`packageId`, `changesetId`) | history endpoints |
| `get_api_reference` | this section of the documentation |

Unlike REST, `apply_changeset` previews unless `dryRun: false` is passed. Results are returned both as JSON text and as structured content. Failures are tool errors carrying the REST error message (operation errors contain `Operation N: …`); `apply_changeset` with a read-only token is a tool error. Bodies are capped at 512 KB like the REST changeset endpoint.
<!-- agent-reference:end -->

### Agent API implementation

Design, decisions and review notes: `docs/AGENT_API_PLAN.md`.

| Component | Location |
|-----------|----------|
| Tokens (service, auth handler, policies, access helpers) | `Infrastructure/AgentApi/` |
| Changeset parser, engine, transactional service | `Infrastructure/AgentApi/Changesets/` |
| Endpoints | `Controllers/Api/Manage/` (read side shared with MCP: `Infrastructure/AgentApi/AgentPackageReader.cs`) |
| MCP tools | `Infrastructure/AgentApi/Mcp/AgentMcpTools.cs` (mapped by `MapAgentMcp`) |
| Entities | `Domain/PersonalAccessToken.cs`, `Domain/PackageChangeset.cs` |
| Token UI | `Components/Account/AgentTokens.razor`, `Components/Pages/Admin/AgentTokensAdmin.razor` |

---

## Implementation

| Component | Location |
|-----------|----------|
| API controllers | `Controllers/Api/V1/` |
| API key auth handler | `Infrastructure/Api/ApiKeyAuthenticationHandler.cs` |
| API key service | `Infrastructure/Api/ApiKeyService.cs` |
| ApiClient entity | `Domain/ApiClient.cs` |
| Rate limiting (both layers) | `Infrastructure/RateLimiting/` (`RateLimitingExtensions.AddRateLimiting`, `ClientRateLimiter`) |
| Nginx rate limiting | `infra/nginx/questions.com.ua.conf` |
| Admin key management | `Components/Pages/Admin/ApiKeys.razor` |
