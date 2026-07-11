# Import Debugging

Developer guide for diagnosing DOCX import problems — when a package imports with missing
questions, mis-detected tours, or content landing in the wrong field. For the feature
overview and pipeline, see [`PACKAGE_IMPORT.md`](PACKAGE_IMPORT.md).

## Job artifacts on disk

Every import leaves its intermediate files under `uploads/jobs/{jobId}/`. These are the
fastest way to see what the parser actually received and produced:

| Path | Contents |
|------|----------|
| `input/<original>.docx` | The uploaded file |
| `working/extracted.json` | `{"Blocks":[...]}` — the serialized `List<DocBlock>` produced by `DocxExtractor` (one entry per paragraph: text + formatting). **This is the parser's input.** |
| `output/package_import.json` | The parsed `ParseResult` (Tours → Questions). Inspect to see exactly what got created. |
| `assets/` | Images extracted from the document |

Find a job by document name by searching `uploads/jobs/*/input/` for the filename.

> `extracted.json` stores Cyrillic as `\uXXXX` escapes. When dumping it with Python on
> Windows, set `PYTHONIOENCODING=utf-8` (and/or `sys.stdout.reconfigure(encoding='utf-8')`)
> or printing fails with a `charmap` `UnicodeEncodeError`.

## Replay the parser offline (no DOCX needed)

Because `working/extracted.json` is the parser's input, you can re-run the parser against
real failing data without re-uploading or re-extracting the DOCX. Deserialize the blocks and
call `PackageParser.Parse` directly:

```csharp
private sealed class Extracted { public List<DocBlock> Blocks { get; set; } = []; }

var json = File.ReadAllText(@"...\uploads\jobs\{jobId}\working\extracted.json");
var extracted = JsonSerializer.Deserialize<Extracted>(json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var result = new PackageParser(NullLogger<PackageParser>.Instance).Parse(extracted.Blocks, []);
// inspect result.Tours / result.Tours[i].Questions
```

`DocBlock` (`Infrastructure/Import/ImportModels.cs`) fields: `Index`, `Text`, `StyleId`,
`IsBold`, `IsItalic`, `IsHeading`, `FontSizeHalfPoints`, `Assets`.

Practical workflow: drop the snippet into a throwaway xUnit `[Fact]` with `ITestOutputHelper`,
run it with `--filter`, confirm the fix, then delete the test. Once the behaviour is
understood, encode it as a permanent synthetic-block test (see below).

The same replay works for Своя гра imports — substitute `ShvagerParser` for `PackageParser`
(the job's parser is chosen by `PackageImportJob.Type`, i.e. the upload zone the user picked).

## Parser structure

`PackageParser` is a **`partial class`** under `QuestionsHub.Blazor/Infrastructure/Import/`,
split by concern:

| File | Responsibility |
|------|----------------|
| `PackageParser.cs` | Entry point, `ParserContext`, main line loop |
| `PackageParser.TourParsing.cs` | Tour / block / warmup detection, numbering modes |
| `PackageParser.QuestionParsing.cs` | Question-start detection, handouts, host instructions |
| `PackageParser.LabelRouting.cs` | Routing `Відповідь:` / `Коментар:` / `Джерело:` etc. into fields |
| `PackageParser.AssetAssociation.cs` | Attaching images to questions |
| `PackageParser.Finalization.cs` | Numbering validation, header parsing, cleanup |
| `PackageParser.Utilities.cs` | Shared helpers |
| `ParserPatterns.cs` | All regexes (both parsers, incl. the Своя гра section) |

`ShvagerParser` (Своя гра) is a **single-file sibling** consuming the same `DocBlock` stream.
It is much simpler: no numbering cascade, no blocks, no format locking. Its failure modes are
theme-boundary detection (see the «Теми:» anchor rules in [PACKAGE_IMPORT.md](PACKAGE_IMPORT.md))
and value-order warnings. Tests: `ImportParsing/Parsing/ShvagerParserTests.cs` (synthetic) and
`ImportParsing/Golden/ShvagerGoldenTests.cs` (runs both real samples from `TestData/Shvager/`).

## Common failure mode: the numbering cascade

**Numbering is strict and sequential.** `IsValidNextQuestionNumber`
(`PackageParser.Finalization.cs`) enforces either a **Global** sequence (1..N across all tours)
or **PerTour** (restart at 1 each tour). A line that looks like a question but whose number
does **not** match the expected next number is **rejected and absorbed as content of the
previous question** — silently, with no error.

The trap is the cascade: if one real question is mis-detected (e.g. written in a different
format than its neighbours), it gets skipped, the counter is now off by one, and **every
following question fails the sequence check too** — including all later tours.

**Symptom:** "only the first N questions imported; the rest became one giant comment on
question N; later tours are empty." When you see this, the first thing to find is the single
question that broke the sequence — not N separate problems.

## Format consistency and duplets

Each tour locks onto a question format at its first question:

- **Named** — `Запитання N.` / `Питання N:`
- **Numbered** — bare `N.`

Once locked, off-format lines are demoted to content. This is deliberate: it keeps duplet
sub-items (`1.` / `2.` listed inside a single question) from being parsed as separate
questions.

The edge case to watch for: a **genuine** question written in bare `N.` form inside an
otherwise Named tour. It is allowed through only when the current question is already complete
(has an answer), which distinguishes it from a pre-answer duplet sub-item. A duplet sub-item
appears before the answer and restarts at 1, so it still stays as content. If you change this
logic, cover both cases in tests.

## Своя гра (Shvager) theme-boundary failure modes

The bare-title format (no explicit `Тема:` prefix) detects a theme by a plausible title line
standing right before the first `10.` question. Three real-world header shapes broke this; all
are now handled (with regression tests), but the mechanics are worth knowing:

- **Author line between the title and `10.`** — headers commonly read `Миші` / `Автор: Тарас
  Вахрів` / (blank) / `10.…`, or with a parenthetical author `Останівка` / `(Авторка – Вікторія
  Маландіна)` / `10.…`. `NextQuestionValueIs10` only looked at the *immediately* following
  non-blank line, so the **author line got parsed as the theme title** and the real title was
  lost. Fix: `NextQuestionValueIs10` skips theme-author designation lines (via
  `TryMatchThemeAuthorLine`), and the theme-header handler attaches both the `Автор: X` label
  form **and** the parenthetical `(Авторка – X)` form as theme editors (`ShvagerParentheticalAuthor`
  regex, guarded by `LooksLikeAuthorList`). Symptom to recognize: a theme `Title` that is literally
  `Автор: …` or `(Авторка – …)`, or an empty title with a "questions out of order" warning.

- **`Теми:` list authors are single surnames** — list entries look like `4.\tОстанівка
  (Маландіна)`. `LooksLikeAuthorList` requires 2–3 capitalized words (`Ім'я Прізвище`), so a lone
  surname fails and the **anchor keeps `(Маландіна)` glued into its title** → the bare title
  `Останівка` never matches the anchor. That's fine: the title is recovered by the
  before-`10.` fallback, and the authoritative editor comes from the real header's author line
  (full name), not the list. Don't "fix" this by loosening `LooksLikeAuthorList` — it would let
  non-author parentheticals become authors. Expect a benign `У списку «Теми:» N тем, а в пакеті
  знайдено M` count-mismatch warning when a reserve theme (`Запас`) isn't in the list.

- **Decoupled handout bracket** — `10. Роздатка:` (bare marker → section becomes Handout) followed
  by a bare `[` / url / `]` on separate lines. The multiline-bracket state only engaged for
  `[Роздатка…` (keyword *inside* the bracket), so the bare `[` never opened it, `]` was never a
  close, and **every line after `]` — including the question text — was swallowed into
  `HandoutText`**. Fix: `TryProcessHandoutBracket` treats a bare `[…]` while already in the Handout
  section as the handout wrapper and reverts to question text after `]`.

## Tests

Parser tests use synthetic blocks built with the `Block("...")` helper in
`QuestionsHub.UnitTests/ImportParsing/Parsing/PackageParserTests.cs`. Prefer reproducing a bug
as a synthetic-block test rather than depending on a real DOCX.

```bash
dotnet test QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj --filter "FullyQualifiedName~PackageParserTests"
```

Run from the repo root; quote project paths in PowerShell.
