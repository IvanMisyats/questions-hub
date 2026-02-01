# Package Import

This document describes the automatic import feature for question packages from DOCX files.

## Overview

Editors can upload tournament packages in DOCX format. The system automatically parses the document structure, extracts questions, answers, comments, and images, then creates a new package in Draft status for review and editing.

## Supported Formats

| Format | Support |
|--------|---------|
| DOCX | Native parsing via OpenXML |
| DOC | Not supported (convert to DOCX manually) |
| PDF | Not supported |

> **Note:** DOC support via automatic conversion is a potential future feature if needed.

**File size limit:** 50 MB

## Import Process

### User Flow

1. Editor navigates to **Мої пакети** (`/manage/packages`)
2. Uploads a DOC/DOCX file using the import form
3. System creates an import job and shows it in the jobs list
4. Job processes in the background (typically 5-30 seconds)
5. On success, editor clicks to open the new draft package
6. Editor reviews and edits the package as needed
7. Editor publishes the package when ready

### Processing Pipeline

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  Validating │───▶│  Extracting │───▶│   Parsing   │───▶│  Importing  │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
                                                                │
                                                                ▼
                                                        ┌─────────────┐
                                                        │  Succeeded  │
                                                        └─────────────┘
```

### Processing Steps

| Step | Description | Duration |
|------|-------------|----------|
| **Validating** | Check file format, size, and create job folder | < 1 sec |
| **Extracting** | Parse DOCX with OpenXML, extract text blocks and images | 2-10 sec |
| **Parsing** | Apply rules to detect package structure (tours, questions, fields) | < 1 sec |
| **Importing** | Create Package, Tours, Questions in database | < 1 sec |
| **Finalizing** | Move images to media folder, save original file | < 1 sec |

**Total time:** 5-15 seconds depending on file size and complexity

## Job Statuses

| Status | Description | User Action |
|--------|-------------|-------------|
| **Queued** | Job is waiting to be processed | Wait |
| **Running** | Job is being processed (see current step) | Wait |
| **Succeeded** | Package created successfully | Open draft package |
| **Failed** | Processing failed (see error message) | Fix file and retry |
| **Cancelled** | Job was cancelled | Upload new file |

## Error Handling

### Common Errors

| Error | Cause | Solution |
|-------|-------|----------|
| Непідтримуваний формат файлу | File is not DOCX | Upload DOCX file (convert DOC to DOCX manually) |
| Файл занадто великий | File exceeds 50 MB | Reduce file size |
| Файл захищений паролем | Document is password-protected | Remove password protection |
| Файл пошкоджений | Document is corrupted | Recreate or repair the file |
| Не вдалося визначити структуру | Parser couldn't detect tours/questions | Check document format |

### Retry Behavior

Some errors trigger automatic retry:

- **Retriable:** Network errors, temporary failures
- **Not retriable:** Invalid format, corrupted file, password-protected

Retry schedule: immediate → 30 seconds → 2 minutes (max 3 attempts)

## Document Structure Recognition

The parser recognizes common Ukrainian question package formats.

### Expected Structure

```
Package Title
Editors: Name1, Name2
Preamble/Description

Тур 1

Блок 1
Редактор - Block Editor Name

1. Question text
   Відповідь: Answer
   Залік: Accepted alternatives
   Незалік: Rejected answers
   Коментар: Commentary
   Джерело: Sources
   Автор: Author name

Блок 2
Редакторка - Another Editor

2. Next question...

Тур 2
Editors of tour (optional)
Tour preamble (optional)

1. Question text
   Відповідь: Answer
   ...
```

### Recognized Labels

| Ukrainian | Field |
|-----------|-------|
| Відповідь | Answer |
| Залік | Accepted Answers |
| Незалік, Не залік, Не приймається | Rejected Answers |
| Коментар | Comment |
| Джерело, Джерела | Source |
| Автор, Автори | Authors |
| Редактор, Редактори | Editors |
| Роздатка, Роздатковий матеріал | Handout |
| [Ведучому: ...] | Host Instructions |

### Tour Detection

Tours are detected by patterns like:
- `Тур 1`
- `Тур: 1`
- `- Тур 1 -`
- `ТУР 1`
- `Тур 1. Назва туру` (with inline preamble/name)
- `Тур 2: Лірики` (with inline preamble/name)

When a tour header includes text after the number (e.g., `Тур 1. Фізики`), this text becomes the tour's preamble.

**Warmup Tour Detection:**
The parser also detects warmup tours using patterns like:
- `Розминка`
- `Warmup`
- `Тур 0`
- `Розминковий тур`

When a warmup tour is detected, it is automatically placed first in the tour order (OrderIndex = 0) and marked with `IsWarmup = true`.

### Block Detection

Tours can optionally contain blocks - subdivisions within a tour with their own editors. Blocks are detected by patterns like:
- `Блок 1`
- `Блок 2.`
- `Блок`

Block editors are recognized by patterns like:
- `Редактор - Name`
- `Редакторка - Name`
- `Редактор блоку: Name`
- `Редакторка блоку: Name`

When blocks are present, questions are associated with the block rather than directly with the tour.

### Question Detection

Questions are detected by patterns like:
- `1. Question text`
- `Питання 1.`
- `Запитання 1.`
- `Запитання №1` (with № symbol)
- `Питання №1.` (with № symbol)

### Text Normalization

During parsing, certain text normalization is applied:

| Character | Replacement | Applies To |
|-----------|-------------|------------|
| Non-breaking space (U+00A0) | Regular space | All text |
| En dash (–), Em dash (—) | Hyphen (-) | All text |
| Combining acute accent (U+0301) | Removed | **Author and Editor names only** |

**Important:** Acute accent marks (stress marks) are **preserved** in question text, answers, comments, and all other fields. They are only stripped from author and editor names to ensure consistent matching in the database.

### Numbering Mode Detection

The parser automatically detects the question numbering mode based on the question numbers in the document:

| Mode | Detection Criteria | Result |
|------|-------------------|--------|
| **Global** | Question numbers are sequential across all tours (1, 2, 3, ... 36) | Questions renumbered globally |
| **PerTour** | Question numbers restart at 1 in each tour | Questions numbered 1..N per tour |
| **Manual** | Any non-numeric question numbers (e.g., "A", "F", "0") | Numbers preserved as-is |

The detected mode is saved to `Package.NumberingMode` and affects how questions are renumbered during editing.

## Image Handling

Images embedded in the document are:

1. Extracted during the Extracting step
2. Saved to the job's assets folder
3. Associated with the nearest question (as handout or comment image)
4. Moved to `/uploads/handouts/` on success
5. Accessible via `/media/{filename}` URL

### Image Association Rules

- Image near "Роздатка" → `HandoutUrl`
- Image near "Коментар" → `CommentAttachmentUrl`
- Unclassified image → `HandoutUrl` (with warning)

## LLM Normalization (Optional)

When the rule-based parser has low confidence, the system may use OpenAI GPT-4o-mini to help structure the content.

### LLM is used when:

- Answer is missing from a question
- Question text couldn't be detected
- Parser confidence is below 50%
- Too many parsing warnings

### Cost

- Model: GPT-4o-mini
- Typical cost: $0.01-0.02 per package
- Maximum: $0.05 per package (guardrail)

## File Storage

### During Processing

```
/uploads/jobs/{jobId}/
├── input/           # Original uploaded file
├── working/         # Intermediate files (converted.docx, extracted.json)
├── assets/          # Extracted images
├── output/          # Final parsed structure
└── logs/            # Processing log
```

### After Success

- Original file: `/uploads/packages/{packageId}/original.docx`
- Images: `/uploads/handouts/{jobId}_img_001.png`

### Cleanup

Job folders are kept for debugging. Cleanup is handled separately (not in MVP).

## Concurrency

- Maximum 2 jobs processed simultaneously
- Jobs are processed in order of creation (FIFO)
- Multiple users can upload files concurrently

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Docker Compose                            │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────┐                         ┌─────────────┐ │
│  │  web (Blazor) │────────────────────────▶│  postgres   │ │
│  │ BackgroundSvc │                         │   (jobs)    │ │
│  └───────────────┘                         └─────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Components

| Component | Responsibility |
|-----------|----------------|
| **Upload Handler** | Validate file, create job, save to disk |
| **Background Service** | Poll for jobs, manage concurrency |
| **DOCX Extractor** | Parse document, extract images |
| **Package Parser** | Apply rules to detect structure |
| **DB Importer** | Create entities in database |

## Configuration

### appsettings.json

```json
{
  "PackageImport": {
    "MaxFileSizeBytes": 52428800,
    "AllowedExtensions": [".docx"],
    "JobTimeoutMinutes": 10,
    "MaxConcurrentJobs": 2
  }
}
```

## Troubleshooting

### Job stuck in "Running"

If the application restarts during processing, jobs are automatically marked as failed on next startup.

### Parser doesn't detect structure

1. Ensure document follows standard format
2. Check that tour/question labels are on separate lines
3. Try adding explicit "Тур 1", "Відповідь:" labels

### Images not extracted

1. Images must be embedded in the document (not linked)
2. Check that images are in supported formats (PNG, JPEG, GIF, WebP)

## Future Improvements

- DOC support via automatic conversion (Gotenberg/LibreOffice)
- PDF support with OCR
- Auto-retry failed jobs from UI
- Job folder cleanup scheduler

