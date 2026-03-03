# Search Functionality

## Overview

Questions Hub provides full-text search across all published questions using PostgreSQL's built-in search capabilities with Ukrainian language support.

## Features

| Feature | Description |
|---------|-------------|
| **Prefix Search** | Find words by their beginning (сепул → сепульки, сепулькарії, сепулювання) |
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

### Search Strategy (3-tier hybrid)

The search uses three complementary matching strategies, combined with OR:

1. **Full-word FTS** (`websearch_to_tsquery` + `@@`) — morphological matching via Ukrainian hunspell dictionary. Handles word forms (відмінки, роди, числа) for words in the dictionary.

2. **Prefix FTS** (`to_tsquery('simple', 'term:*')` + `@@`) — matches any word starting with the search term. Handles proper nouns, foreign words, and partial word search. Built by `SearchQueryParser.BuildPrefixTsquery()`.

3. **Word-level trigram** (`word_similarity` + `<%`) — fuzzy matching for typo tolerance. Uses `word_similarity()` instead of `similarity()` to match at the word level within the document rather than the whole document.

### Ranking

Results are ranked by a weighted combination:

| Strategy | Weight | Purpose |
|----------|--------|---------|
| Full-word FTS (`ts_rank_cd`) | 4x | Exact morphological matches rank highest |
| Prefix FTS (`ts_rank_cd`) | 2x | Prefix matches rank medium |
| Word-level trigram (`word_similarity`) | 1x | Fuzzy matches rank lowest |

### PostgreSQL Extensions

- **`unaccent`** - Removes diacritical marks (accents)
- **`pg_trgm`** - Trigram matching for fuzzy search and `word_similarity`

### Text Search Configuration

Uses a custom `ukrainian` text search configuration with:
- **hunspell_uk dictionary** - Ukrainian morphological analysis
- **Normalization** - Lowercase, accent removal, ґ→г normalization

### Database Columns

The `Questions` table has generated columns:
- `SearchTextNorm` - Normalized concatenated text for trigram matching
- `SearchVector` - tsvector for full-text search with field weights

### Indexes

- GIN index on `SearchTextNorm` for trigram operations (`%`, `<%` operators)
- GIN index on `SearchVector` for full-text search (`@@` operator — used by both full-word and prefix tsqueries)

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
    string Text,
    string Answer,
    string? HandoutText,
    string? AcceptedAnswers,
    string? RejectedAnswers,
    string? Comment,
    string? Source,
    string TextHighlighted,           // Text with <mark> tags around matched terms
    string AnswerHighlighted,
    string? HandoutTextHighlighted,
    string? AcceptedAnswersHighlighted,
    string? RejectedAnswersHighlighted,
    string? CommentHighlighted,
    string? SourceHighlighted,
    double Rank                        // Relevance score
);
```

The `*Highlighted` fields contain the same text as the original fields but with `<mark>` tags around matched search terms. Use `HighlightSanitizer.Sanitize()` to safely render these in Blazor.

### Highlighting Strategy

The search uses a hybrid approach for highlighting:

1. **Server-side (PostgreSQL ts_headline)**: Highlights words matched by the combined full-word + prefix tsquery. When a prefix like "сепул" matches, the entire matched word ("сепульки") is highlighted.

2. **Client-side fallback (HighlightSanitizer)**: When ts_headline doesn't produce highlights (e.g., for accented words like "Копенга́гена" when searching "Копенгаген"), the sanitizer applies accent-insensitive character-level matching. This also handles prefix highlighting at the substring level.

This ensures:
- Words with accents (stress marks) are properly highlighted
- Original text is preserved with its accents
- Both morphological variants, prefix matches, and exact matches are highlighted
- Proper nouns not in the Ukrainian dictionary are still findable by prefix

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

