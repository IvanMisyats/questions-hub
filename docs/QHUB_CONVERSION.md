# Converting external packages to `.qhub` offline

Use this when you get a batch of third-party packages (DOCX, TXT, SIGame-style JSON) and need
ready-to-upload `.qhub` files instead of uploading each DOCX through the site. Reasons to go
offline: merging several files into one package, fixing a layout the parser can't read, or
adding metadata the source lacks (author, round structure).

Schema: [`PACKAGE_FORMAT.md`](PACKAGE_FORMAT.md). Parser rules: [`PACKAGE_IMPORT.md`](PACKAGE_IMPORT.md).

## The harness

Use a small console app that references the Blazor project, so the conversion runs the
**site's own** extractor, parser and importer rather than a re-implementation:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
    <ProjectReference Include="<repo>\QuestionsHub.Blazor\QuestionsHub.Blazor.csproj" />
  </ItemGroup>
</Project>
```

The working copy (not in the repo) is in Ivan's OneDrive at `www/_toImport_si/qhub/_converter/harness`,
next to the converted files and their `README.md`. Its modes:

| Mode | Does |
|---|---|
| `parse-docx <in.docx> <out.json>` | `DocxExtractor` → `Normalize` pre-pass → `ShvagerParser`, and dumps the `ParseResult` |
| `parse-txt <in.txt> <out.json>` | One `DocBlock` per line → `ShvagerParser` |
| `merge <spec.json> <out.qhub>` | Parses several DOCX files in order, renumbers themes 1..N and writes one `.qhub`. The spec is `{title, preamble, editors[], docx[]}` |
| `verify <report.json> <f.qhub>…` | `QhubExtractor.Extract` on each file and reports warnings |
| `dbimport <f.qhub>…` | Extractor → `PackageDbImporter` into an in-memory DB, which catches DB-stage failures that `verify` can't |

Mechanics that aren't obvious:

- **Build with `-c Release`** when the dev app is running. It locks `bin/Debug` of the referenced project.
- **Set `Console.OutputEncoding = UTF8`** in the harness, or Cyrillic output is mangled.
- **Serialize through the `Qhub*` classes** (`Infrastructure/Import/QhubModels.cs`), never
  through hand-rolled JSON. `QhubExtractor` reads with `PropertyNameCaseInsensitive = false`, so
  the `[JsonPropertyName]` attributes matter. Use `DefaultIgnoreCondition = WhenWritingNull` and
  `UnsafeRelaxedJsonEscaping`, and follow `QhubExporter.MapToQhubPackage`: `formatVersion` "1.1",
  `gameType` "shvager" (null for Що?Де?Коли?), `numberingMode` null when Global, and empty strings collapsed to null.
- **Pack only the assets that `package.json` references.** The parser drops images it can't
  attach, and those would otherwise be packed as dead weight. Extracted asset file names are
  content-hashed, so regenerating a package is deterministic. `QhubExtractor` rejects a
  per-entry compression ratio above 100 and more than 500 MB decompressed in total. Upload cap: 50 MB.
- **In-memory `dbimport`**: subclass `QuestionsHubDbContext` and `Ignore` `Question.SearchVector`
  and `Question.SearchTextNorm`, because those are Postgres FTS columns.
  `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`, and give each
  run a fresh `InMemoryDatabaseRoot`.

## Layouts the parser does not read: normalize first

**`ShvagerParser` silently returns 0 themes and 0 warnings** on this "bot" layout, seen in the
Борисфен Лайт 2026 «ДЛЯ БОТА» DOCX files. The SIGame TXT twins had the same bare value lines.

```
1 тема
Прапор
                        ← optional «Коментар: …» or a prose line = theme preamble
10

14-го березня 1990-го року ЦЕ МІСТО …
```

Uploading such a DOCX to the site would produce an empty package. That's a candidate parser
improvement. Until then, the harness's `Normalize` rewrites the `DocBlock` list before parsing:

- `N тема` + the title on the next non-empty line → `Тема: <title>`, and the title line is blanked.
- A `Коментар:` or prose line between the title and the first question → `Преамбула: …`.
- A bare `10` / `20` … line + the next non-empty line → `10. <text>`.
- An identical `Форма:` line repeated straight after itself → dropped.

Blank the consumed lines (`block with { Text = "" }`) instead of removing them, so block
indexes stay stable.

## Verify: three checks, all required

1. **Line coverage**: every non-empty source line (labels stripped) occurs somewhere in the
   parsed fields. **Fold before comparing**: the parser normalizes apostrophes (`'` `’` → `ʼ`) and
   en/em dashes (→ `-`), and the source contains HTML entities (`&quot;`). Without folding, this
   check reports dozens of false "missing" lines.
2. **Round-trip**: run `QhubExtractor` on the output and diff it against the DOCX-derived
   `ParseResult`, treating `""` and `null` as equal (the parser emits `""` for absent fields, the
   exporter convention emits `null`). Every other difference is a real defect.
3. **`dbimport`** passes, and the author list it prints has no junk. In the TXT twins, `Ім'я
   Прізвище (Кривий Ріг)` once became an author called «Кривий Ріг».

## Conventions agreed with Ivan

- **Multi-round tournaments → one package.** Flatten the themes to 1..N, and record the round
  split in the package preamble (`Тур 1 — теми 1–8\n…`, or `Півфінал — теми 31–38, перестрілка — теми 39–40.`).
- **Tie-break themes**: title prefix `Перестрілка: <назва>`. A regular theme that is merely
  *named* «Перестрілка» stays as it is.
- **Secret themes** («Поки секрет»): keep that title, and put the reveal line («Ваша тема: …»)
  at the end of the 10-point question's comment.
- **Single-author tournaments**: put the author in package `editors` with `sharedEditors: true`,
  and cascade to every theme's `editors` and every question's `authors`. **Never guess an author
  the source doesn't name.** Ask, even when earlier years of the same tournament had one.
- **File names**: full name + year with no spaces, e.g. `БорисфенЛайт2026.qhub` or
  `ШвагерЛігаВесна2026.qhub`, not abbreviations like `БорЛайт2026`.
- **Where outputs go**: the OneDrive `www/_toImport_si/qhub/` folder, with a section in its `README.md` per batch.

## Source-specific notes

- **SIGame-style JSON** has no sources and no per-question authors. Where a TXT twin exists, take
  those fields from it, matching questions by normalized text. Answers carry inline
  `Залік:` / `Незалік:` / `(Форма: …)` / `[Ведучому: …]` that must be split into fields. Watch
  for literal `\n` (backslash-n) in strings, and for «Вопрос недоступен» placeholder questions.
- **Embedded media links**: download them and pack them as assets, so the packages don't depend
  on image hosts.
  - **Dead hosts**: funkyimg.com now redirects to Notion, and its og:image is the Notion banner,
    so reject it. i.piccy.info no longer resolves, and prntscr.com is gone.
  - **Wikimedia** serves only standard thumbnail widths (use 1280px) and returns 429 to
    browser-like User-Agents.
  - **ibb.co** returns 503 under parallel fetches, so use about 2 workers and retry with backoff.
  - **Private Google Drive files** return a sign-in page. Keep any unrecoverable handout visible
    as `[Роздатковий матеріал: URL]`.
