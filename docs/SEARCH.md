# Search Functionality

## Overview

Questions Hub provides full-text search across all published questions using PostgreSQL's built-in search capabilities with Ukrainian language support.

## Features

| Feature | Description |
|---------|-------------|
| **Ukrainian Morphology** | Finds words in different forms (відмінки, роди, числа) |
| **Accent Insensitive** | Searches ignore Ukrainian accents (А́мундсен = Амундсен) |
| **Typo Tolerance** | Finds results even with spelling mistakes |
| **Multi-word Search** | Search for multiple words at once |
| **Phrase Search** | Use quotes for exact phrase matching |
| **Boolean Operators** | Support for AND (default), OR, exclusion |
| **Result Highlighting** | Matched words are highlighted in results |
| **Relevance Ranking** | Results sorted by relevance score |

## Search Syntax

| Syntax | Description | Example |
|--------|-------------|---------|
| `word1 word2` | Both words required (AND) | `Амундсен Антарктида` |
| `word1 OR word2` | Either word (OR) | `Амундсен OR Скотт` |
| `"exact phrase"` | Exact phrase match | `"Південний полюс"` |
| `-word` | Exclude word | `Амундсен -Скотт` |

## Searchable Fields

The search looks in the following question fields (in order of priority):

1. **Question Text** (highest priority)
2. **Handout Text**
3. **Answer**
4. **Accepted Answers**
5. **Rejected Answers**
6. **Comment** (lowest priority)

## Technical Implementation

### PostgreSQL Extensions

- **`unaccent`** - Removes diacritical marks (accents)
- **`pg_trgm`** - Trigram matching for fuzzy search

### Text Search Configuration

Uses a custom `ukrainian` text search configuration with:
- **hunspell_uk dictionary** - Ukrainian morphological analysis
- **Normalization** - Lowercase, accent removal, ґ→г normalization

### Database Columns

The `Questions` table has generated columns:
- `SearchTextNorm` - Normalized concatenated text for trigram matching
- `SearchVector` - tsvector for full-text search with field weights

### Indexes

- GIN index on `SearchTextNorm` for trigram operations
- GIN index on `SearchVector` for full-text search

## Setup Requirements

### Dictionary Files

Ukrainian dictionary files must be present in `db/dictionaries/` folder:
- `uk_UA.dic` - Dictionary words
- `uk_UA.aff` - Affix/morphology rules

Database setup scripts are in `db/scripts/`:
- `01-extensions.sql` - Installs required PostgreSQL extensions
- `02-user-permissions.sql` - Creates user and grants permissions
- `03-fts-setup.sql` - Configures FTS dictionary and search configuration

These are mounted into the PostgreSQL container automatically via Docker Compose.

### Database Setup

The `db-setup` container automatically creates:
1. Required PostgreSQL extensions
2. Ukrainian hunspell dictionary
3. Ukrainian text search configuration

## API

### Search Page

Access the search at: `/search` or `/search/{query}`

### SearchService

```csharp
// Inject the service
@inject SearchService SearchService

// Perform search
var results = await SearchService.Search("query", limit: 50);
```

### SearchResult

```csharp
public record SearchResult(
    int QuestionId,
    int TourId,
    int PackageId,
    string PackageTitle,
    string TourNumber,
    string QuestionNumber,
    string Snippet,      // Highlighted text fragment
    double Rank          // Relevance score
);
```

## Troubleshooting

### Search returns no results

1. Check if migration `AddFuzzySearch` was applied
2. Verify Ukrainian FTS config exists: 
   ```sql
   SELECT * FROM pg_ts_config WHERE cfgname = 'ukrainian';
   ```
3. Check if search columns are populated:
   ```sql
   SELECT "Id", "SearchVector" IS NOT NULL FROM "Questions" LIMIT 5;
   ```

### Dictionary errors

1. Verify dictionary files are mounted:
   ```bash
   docker exec questions-hub-db ls -la /usr/local/share/postgresql/tsearch_data/uk_ua*
   ```
2. Re-run db-setup:
   ```bash
   docker compose --profile production run --rm db-setup
   ```

