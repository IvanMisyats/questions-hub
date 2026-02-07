# Package Interchange Format (`.qhub`)

**Version**: 1.0  
**Last Updated**: February 7, 2026

---

## Overview

The Questions Hub Package Format defines how to store a complete question package as a portable, human-readable bundle. It is designed for three primary use cases:

1. **Import from external sites** — download packages from other sources, convert them to this format, then upload to Questions Hub.
2. **LLM-assisted parsing** — an LLM reads a DOCX/PDF and outputs a valid `package.json` in this format.
3. **Transfer between environments** — export a package from a local instance and import it to production.

---

## Format Variants

### Unpacked Folder

A directory containing `package.json` at the root and an optional `assets/` subfolder:

```
my-package/
├── package.json
└── assets/
    ├── handout_q3.png
    ├── handout_q7.jpg
    └── comment_q12.png
```

This is the **preferred form for LLM workflows and troubleshooting** — the JSON is directly readable and editable.

### Packed Archive (`.qhub`)

A ZIP file with the `.qhub` extension containing the same structure. Used for transfer and upload:

```
my-package.qhub          (ZIP archive)
├── package.json
└── assets/
    └── ...
```

To inspect, rename to `.zip` or use any ZIP tool.

> **Note:** LLMs cannot produce ZIP files directly. When using LLMs, work with the unpacked folder. Pack into `.qhub` only for transport.

### JSON-Only Mode (No Local Assets)

When all media is available via external URLs, the `assets/` folder can be omitted entirely. Questions reference media via `handoutAssetUrl` / `commentAssetUrl` fields instead of local filenames. This is common when scraping packages from websites.

---

## `package.json` Schema

### Top-Level Structure

```jsonc
{
  "formatVersion": "1.0",

  // Optional metadata
  "sourceUrl": "https://example.com/pkg/42", // where the package was obtained

  // Package data
  "title": "Назва пакету",
  "description": "Опис пакету",             // optional
  "preamble": "Преамбула пакету",            // optional
  "playedFrom": "2025-11-15",               // optional, ISO 8601 date
  "playedTo": "2025-11-16",                 // optional, ISO 8601 date (null for single-day)
  "numberingMode": "Global",                // optional: "Global" | "PerTour" | "Manual"
  "sharedEditors": false,                   // optional, default false
  "editors": ["Ім'я Прізвище"],             // package-level editors (when sharedEditors=true)
  "tags": ["тег1", "тег2"],                 // optional

  "tours": [ /* ... */ ]
}
```

### Tour Object

```jsonc
{
  "number": "1",                            // display number (string)
  "isWarmup": false,                        // optional, default false
  "editors": ["Ім'я Прізвище"],             // tour editors (when sharedEditors=false)
  "preamble": "Преамбула туру",             // optional
  "comment": "Коментар до туру",            // optional
  "questions": [ /* ... */ ],               // questions directly in tour
  "blocks": [ /* ... */ ]                   // optional blocks within tour
}
```

**Ordering**: Tours are ordered by their position in the `tours` array (index 0 = first tour). No explicit `orderIndex` field is needed — array position is the source of truth.

**Warmup**: At most one tour may have `"isWarmup": true`. If present, it should be the first element in the array.

**Blocks vs Questions**: A tour contains either:
- `questions` only (no blocks) — the common case
- `blocks` with questions inside them — when the tour is subdivided
- Both `questions` and `blocks` — questions outside any block ("orphan" questions) plus block-grouped questions

### Block Object

```jsonc
{
  "name": "Назва блоку",                    // optional (display as "Блок N" if absent)
  "editors": ["Ім'я Прізвище"],             // block editors
  "preamble": "Преамбула блоку",            // optional
  "questions": [ /* ... */ ]
}
```

**Ordering**: Blocks are ordered by their position in the `blocks` array.

### Question Object

```jsonc
{
  "number": "1",                            // display number (string), always present

  // Content fields
  "hostInstructions": "Вказівка ведучому",  // optional, not read to players
  "handoutText": "Текст роздатки",          // optional, text portion of handout
  "text": "Текст запитання",                // required
  "answer": "Відповідь",                    // required
  "acceptedAnswers": "Залік",               // optional
  "rejectedAnswers": "Незалік",             // optional
  "comment": "Коментар",                    // optional
  "source": "Джерело",                      // optional
  "authors": ["Ім'я Прізвище"],             // question authors

  // Media — local asset filenames (relative to assets/ folder)
  "handoutAssetFileName": "handout_q3.png", // optional
  "commentAssetFileName": "comment_q3.png", // optional

  // Media — external URLs (alternative to local assets)
  "handoutAssetUrl": "https://example.com/img/q3.png",   // optional
  "commentAssetUrl": "https://example.com/img/q3_a.png"  // optional
}
```

**Ordering**: Questions are ordered by their position in the `questions` array.

**Media fields**: A question may reference media in two ways:
- **Local file** via `handoutAssetFileName` / `commentAssetFileName` — file must exist in `assets/`.
- **External URL** via `handoutAssetUrl` / `commentAssetUrl` — a publicly accessible URL.

If both local and URL variants are provided for the same slot, the local file takes precedence during import.

---

## Field Reference

### Package Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `formatVersion` | string | ✅ | Always `"1.0"` |
| `sourceUrl` | string | — | URL where the package was obtained |
| `title` | string | ✅ | Package title |
| `description` | string | — | Package description or notes |
| `preamble` | string | — | Preamble (testers, acknowledgements) |
| `playedFrom` | string | — | Start date, ISO 8601 (`"YYYY-MM-DD"`) |
| `playedTo` | string | — | End date, ISO 8601 (`"YYYY-MM-DD"`). Omit for single-day events |
| `numberingMode` | string | — | `"Global"`, `"PerTour"`, or `"Manual"`. Default: importer decides |
| `sharedEditors` | boolean | — | `true` = editors at package level; `false` (default) = per-tour |
| `editors` | string[] | — | Package-level editors. Meaningful when `sharedEditors=true` |
| `tags` | string[] | — | Tag labels (e.g., `["2025", "ЛУК"]`) |
| `tours` | Tour[] | ✅ | Array of tours, ordered by display order |

### Tour Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `number` | string | ✅ | Display number (`"0"` for warmup, `"1"`, `"2"`, …) |
| `isWarmup` | boolean | — | `true` if this is a warmup tour. Default: `false` |
| `editors` | string[] | — | Tour editors (when `sharedEditors=false`) |
| `preamble` | string | — | Preamble text |
| `comment` | string | — | Tour-level commentary |
| `questions` | Question[] | — | Questions directly under the tour |
| `blocks` | Block[] | — | Optional blocks within the tour |

### Block Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | — | Block name. If absent, displayed as "Блок N" |
| `editors` | string[] | — | Block editors |
| `preamble` | string | — | Preamble text |
| `questions` | Question[] | ✅ | Questions within the block |

### Question Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `number` | string | ✅ | Display number (`"1"`, `"13"`, `"F"`, etc.) |
| `hostInstructions` | string | — | Instructions for the host (not read aloud) |
| `handoutText` | string | — | Text portion of handout material |
| `handoutAssetFileName` | string | — | Filename in `assets/` for handout media |
| `handoutAssetUrl` | string | — | External URL for handout media |
| `text` | string | ✅ | Main question text |
| `answer` | string | ✅ | Correct answer |
| `acceptedAnswers` | string | — | Alternative accepted answers (залік) |
| `rejectedAnswers` | string | — | Explicitly rejected answers (незалік) |
| `comment` | string | — | Commentary explaining the answer |
| `commentAssetFileName` | string | — | Filename in `assets/` for comment media |
| `commentAssetUrl` | string | — | External URL for comment media |
| `source` | string | — | Source references |
| `authors` | string[] | — | Question authors |

---

## Conventions

### Author Names

Authors and editors are represented as `"FirstName LastName"` strings (two words, space-separated).

```json
["Андрій Пундор", "Іван Місяць"]
```

If the source has more complex attribution (e.g., "в редакції Івана Місяця", "за ідеєю Андрія Пундора"), include the full phrase in the author string. The importer will attempt to parse it.

### Text Content

- **Newlines**: Use `\n` for line breaks within fields. Multi-paragraph content (questions, comments, sources) preserves blank lines as `\n\n`.
- **Encoding**: UTF-8 without BOM.
- **No HTML**: Plain text only. No HTML tags in content fields.

### Null vs Absent

Optional fields can be either:
- Omitted from the JSON entirely (preferred — keeps files compact)
- Set to `null` (also valid)
- Set to empty string `""` (treated as absent)

### Asset Filenames

- Filenames should be simple and descriptive: `handout_q3.png`, `comment_q12.jpg`.
- No subdirectories within `assets/` — all files are at the root level.
- Supported formats:
  - **Images**: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`
  - **Video**: `.mp4`, `.webm`
  - **Audio**: `.mp3`, `.ogg`, `.wav`

---

## Complete Example

```json
{
  "formatVersion": "1.0",
  "sourceUrl": "https://example.com/packages/42",

  "title": "Борисфен-12. Верхня Хортиця",
  "description": "Синхронний турнір, 3 тури по 12 запитань",
  "playedFrom": "2025-11-15",
  "playedTo": "2025-11-16",
  "numberingMode": "Global",
  "sharedEditors": false,
  "tags": ["2025", "синхрон"],

  "tours": [
    {
      "number": "0",
      "isWarmup": true,
      "editors": ["Олена Коваленко"],
      "questions": [
        {
          "number": "1",
          "text": "Розминкове запитання. Назвіть столицю України.",
          "answer": "Київ",
          "authors": ["Олена Коваленко"]
        }
      ]
    },
    {
      "number": "1",
      "editors": ["Андрій Пундор", "Марія Шевченко"],
      "preamble": "Тестували: Олег Бондаренко, Ірина Грищенко",
      "questions": [
        {
          "number": "1",
          "text": "ВІН узяв прізвисько на честь виконання партії диявола в опері Оффенбаха. По рації прізвисько звучало довго, тож зрештою було скорочене. Назвіть ЙОГО.",
          "answer": "Сліпак",
          "acceptedAnswers": "Василь Сліпак",
          "comment": "Василь Сліпак — оперний співак, соліст Паризької національної опери, що загинув під час війни на сході України. Відомо, що у нього був позивний «Міф», але насправді — це скорочення від «Мефістофеля».",
          "source": "https://uk.wikiquote.org/wiki/Сліпак_Василь_Ярославович",
          "authors": ["Андрій Пундор"]
        },
        {
          "number": "2",
          "hostInstructions": "Текст у дужках читати подумки!",
          "handoutText": "Все, що ви скажете, може бути використано проти вас.",
          "text": "В оповіданні Філіпа Діка підозрюваному (зачитують права, фрагмент яких ми вам роздали). Яке слово ми замінили на роздатці?",
          "answer": "подумаєте",
          "acceptedAnswers": "думаєте; мислите; замислите",
          "comment": "Дія оповідання відбувається у майбутньому, де існує телепатичний зв'язок.",
          "source": "Філіп Дік «З глибин пам'яті».",
          "authors": ["Іван Місяць"]
        },
        {
          "number": "3",
          "text": "Запитання з ілюстрацією у роздатці.",
          "answer": "Відповідь",
          "handoutAssetFileName": "handout_q3.png",
          "authors": ["Марія Шевченко"]
        }
      ]
    },
    {
      "number": "2",
      "editors": ["Олег Бондаренко"],
      "preamble": "Тестували: Ірина Грищенко",
      "questions": [
        {
          "number": "13",
          "text": "Бліц.\n1. ІКС вважають прообразом сучасного стриптизу. \"ІКС\" вийшов 2010 року. Назвіть ІКС.\n2. У творі Олександра Олеся перед відкриттям ярмарку шумно зводили ЙОГО. Назвіть ЙОГО словом із трьома однаковими голосними.\n3. ВОНА англійською — Slapstick. Назвіть ЇЇ словом італійського походження.",
          "answer": "1. Бурлеск. 2. Балаган. 3. Буфонада.",
          "acceptedAnswers": "3. Буфонатто.",
          "comment": "Бліц присвячено Бу-Ба-Бу — літературному угрупованню, яке існувало з 1985 по 1996 рік.",
          "source": "https://uk.wikipedia.org/wiki/Бу-Ба-Бу",
          "authors": ["Андрій Пундор"]
        },
        {
          "number": "14",
          "text": "Запитання з ілюстрацією у коментарі.",
          "answer": "Відповідь",
          "commentAssetUrl": "https://example.com/img/answer14.jpg",
          "authors": ["Олег Бондаренко"]
        }
      ]
    },
    {
      "number": "3",
      "editors": ["Ірина Грищенко"],
      "blocks": [
        {
          "name": "Дуплети",
          "editors": ["Ірина Грищенко"],
          "questions": [
            {
              "number": "25",
              "text": "Дуплет.\n1. Перше запитання дуплету.\n2. Друге запитання дуплету.",
              "answer": "1. Відповідь 1. 2. Відповідь 2.",
              "authors": ["Ірина Грищенко"]
            }
          ]
        },
        {
          "name": "Основна частина",
          "editors": ["Ірина Грищенко"],
          "questions": [
            {
              "number": "26",
              "text": "Звичайне запитання в блоці.",
              "answer": "Відповідь",
              "authors": ["Ірина Грищенко"]
            }
          ]
        }
      ]
    }
  ]
}
```

---

## LLM Usage Guidelines

When prompting an LLM to parse a DOCX/PDF into this format, use the following guidance.

### Prompt Template

```
You are given the text content of a "Що? Де? Коли?" question package document.
Parse it into a JSON object following the Questions Hub Package Format v1.0.

Output ONLY valid JSON. Do not wrap in markdown code fences.

Key rules:
- "formatVersion" must be "1.0"
- Tours are identified by headings like "Тур 1", "Перший тур", "Тур I", or "Розминка"
- Questions start with a number followed by a period: "1.", "2.", etc.
- Detect these labeled fields after question text:
  - "Відповідь:" → answer
  - "Залік:" → acceptedAnswers
  - "Незалік:" → rejectedAnswers
  - "Коментар:" → comment
  - "Джерело:" / "Джерела:" → source
  - "Автор:" / "Автори:" → authors (split by comma)
  - "[Ведучому:" or "Вказівка ведучому:" → hostInstructions
  - "Роздатка" / "Роздатковий матеріал" → handoutText
- Authors are "FirstName LastName" — always two words
- If numbering restarts each tour → "numberingMode": "PerTour"
- If numbering is continuous across tours → "numberingMode": "Global"
- If numbers are non-standard (hex, letters) → "numberingMode": "Manual"
- Warmup tour (Розминка, Тур 0) → "isWarmup": true, "number": "0"
- Omit optional fields that are not present (do not output null values)
- Preserve original line breaks within text fields as \n
```

### Field Detection Hints

| Document Pattern | JSON Field |
|------------------|------------|
| `[Ведучому: ...]` at start of question | `hostInstructions` (extract text inside brackets) |
| `Роздатка` / `Роздатковий матеріал` section | `handoutText` |
| Image/figure within question | `handoutAssetFileName` (if extractable) or note in text |
| `Відповідь:` line | `answer` |
| `Залік:` line | `acceptedAnswers` |
| `Незалік:` line | `rejectedAnswers` |
| `Коментар:` line(s) | `comment` |
| `Джерело:` / `Джерела:` line(s) | `source` (keep all lines joined with `\n`) |
| `Автор:` / `Автори:` line | `authors` (split by `,` and trim) |
| "Бліц", "Дуплет" before sub-questions | A single question with sub-questions in `text` and numbered answers |

### Handling Complex Question Types

**Blitz / Duplet / Tetrabitz** — these are composite questions where multiple sub-questions count as one:

- The document marks them: "Бліц.", "Дуплет.", etc.
- Sub-questions are numbered within (1., 2., 3.)
- All sub-questions share a single `number` in the tour sequence
- Represent as **one question object** with sub-questions in `text` (preserving sub-numbering) and numbered answers in `answer` / `acceptedAnswers`

---

## Validation Rules

A valid package file must satisfy:

1. `formatVersion` is `"1.0"`
2. `title` is a non-empty string
3. `tours` is a non-empty array
4. Each tour has a non-empty `number`
5. Each tour has at least one question (directly or within blocks)
6. Each question has non-empty `number`, `text`, and `answer`
7. If `handoutAssetFileName` is specified, the file must exist in `assets/`
8. If `commentAssetFileName` is specified, the file must exist in `assets/`
9. Asset files must have a supported extension (`.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.mp4`, `.webm`, `.mp3`, `.ogg`, `.wav`)

---

## JSON Schema

A formal JSON Schema is available at [`package-format.schema.json`](package-format.schema.json) for programmatic validation.
