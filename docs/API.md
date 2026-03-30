# Public API Reference

Read-only API for external clients (mobile apps, integrations).

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
| Nginx | Per IP | 30 req/min on `/api/v1/` |
| ASP.NET | Per API key | 60 req/min (general endpoints) |
| ASP.NET | Per API key | 30 req/min (`/packages/{id}`) |
| ASP.NET | Per API key | 20 req/min (`/search`) |

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
      "description": "Опис пакету...",
      "publicationDate": "2025-12-15T10:30:00Z",
      "playedFrom": "2025-12-01",
      "playedTo": "2025-12-02",
      "questionsCount": 72,
      "editors": [
        { "id": 1, "firstName": "Іван", "lastName": "Петренко" }
      ],
      "tags": [
        { "id": 5, "name": "2025" }
      ]
    }
  ],
  "totalCount": 150,
  "totalPages": 8,
  "currentPage": 1
}
```

---

### `GET /api/v1/packages/{id}`

Full package detail with tours, blocks, and questions.

Returns `404` if the package does not exist or is not public.

**Response:**

```json
{
  "id": 42,
  "title": "Кубок Львова 2025",
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
| `numberingMode` | `global`, `perTour`, `manual` | How question numbers are assigned |
| `tours[].type` | `regular`, `warmup`, `shootout` | Tour type. Warmup is always first, shootout always last |
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
      "tourNumber": "1",
      "questionNumber": "3",
      "text": "Текст запитання...",
      "answer": "Відповідь",
      "handoutText": null,
      "handoutUrl": null,
      "acceptedAnswers": null,
      "rejectedAnswers": null,
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

## Implementation

| Component | Location |
|-----------|----------|
| API controllers | `Controllers/Api/V1/` |
| API key auth handler | `Infrastructure/Api/ApiKeyAuthenticationHandler.cs` |
| API key service | `Infrastructure/Api/ApiKeyService.cs` |
| ApiClient entity | `Domain/ApiClient.cs` |
| Rate limiting config | `Program.cs` (`AddPublicApiServices`) |
| Nginx rate limiting | `infra/nginx/questions.com.ua.conf` |
| Admin key management | `Components/Pages/Admin/ApiKeys.razor` |
