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

## Tests

Parser tests use synthetic blocks built with the `Block("...")` helper in
`QuestionsHub.UnitTests/ImportParsing/Parsing/PackageParserTests.cs`. Prefer reproducing a bug
as a synthetic-block test rather than depending on a real DOCX.

```bash
dotnet test QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj --filter "FullyQualifiedName~PackageParserTests"
```

Run from the repo root; quote project paths in PowerShell.
