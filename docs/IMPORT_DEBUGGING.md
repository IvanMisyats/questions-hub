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

## Replay the whole corpus and diff (do this for any heuristic change)

`uploads/jobs/` accumulates **every** import ever run on the machine, so it is a free
regression corpus of real documents. Parser detection rules are heuristics: a change that
fixes the document in front of you routinely breaks another one, and neither the unit tests
nor the two golden samples will tell you. Replay everything and diff.

Workflow:

1. Write a throwaway `[Fact]` that walks `uploads/jobs/*/`, skips dirs whose
   `output/package_import.json` has the wrong `"Type"` (`1` = Своя гра), replays
   `working/extracted.json`, and dumps **one compact line per tour/question** plus warnings.
2. Capture the output **before** touching the parser — that is the baseline.
3. Make the change, re-run, `diff -u baseline after`. Every hunk must be an intended
   improvement; anything else is a regression.

**Have the harness write the dump to a file** instead of scraping it back out of the test
logger. One `File.WriteAllText` beats a `Select-String` filter that must be kept in sync with
every prefix the dump emits, and it keeps xUnit's own chatter out of the diff:

```csharp
var dump = Path.Combine(Path.GetTempPath(), "shvager_corpus_dump.txt");
File.WriteAllText(dump, sb.ToString(), new UTF8Encoding(false));
output.WriteLine($"written to {dump}");
```

```bash
dotnet test QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj \
  -p:BaseOutputPath=/tmp/qh-testbin/ -p:UseAppHost=false \
  --filter "FullyQualifiedName~ReplayShvagerCorpus"
cp "$(cygpath "$TMP")/shvager_corpus_dump.txt" /tmp/shvager_baseline.txt   # Windows temp → msys
```

Two things about running it:

- **Find `uploads/jobs/` via `[CallerFilePath]`, never by walking up from
  `AppContext.BaseDirectory`.** The walk-up only works while the test runs out of the repo's
  `bin/`. Add the bin-lock workaround `-p:BaseOutputPath=/tmp/qh-testbin/`
  ([`LOCAL_DEV.md`](LOCAL_DEV.md)) — which you will, since the corpus is usually replayed while
  the app is running — and the assembly executes from the temp dir, so the walk-up sails past the
  drive root and throws `DirectoryNotFoundException: uploads/jobs not found`. `[CallerFilePath]`
  is baked in at compile time and is immune (`..` count follows where you put the harness):
  ```csharp
  private static string RepoRoot([CallerFilePath] string thisFile = "")
      => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
  ```
- Run the **rest** of the suite without the slow harness by negating the filter:
  `--filter "FullyQualifiedName!~ReplayShvagerCorpus"`.

Delete the harness once done — it reads a machine-local path (`uploads/` is gitignored),
so it cannot live in the repo. Encode what you learned as synthetic-block tests instead.

Two things that cost time when doing this:

- **A tightened guard can silently revert an earlier win.** Narrowing the implicit-theme-list
  detector to reject numbered *host instructions* also stopped a theme title being recovered in
  an unrelated package. Only the diff showed it — re-diff after **every** tweak, not just at the
  end.
- **Make each dumped line self-identifying** (prefix the package name), and don't trust
  position alone: in a flat sectioned dump a package's trailing warning sits directly above the
  *next* `###` header and reads as if it belongs to it. Misattributing one cost a full
  investigation round-trip.
- **Tracing from inside parser code:** `Console.WriteLine` is *not* interleaved with
  `ITestOutputHelper` in the console logger, so temporary traces vanish from the filtered
  output. Push them through the result object instead (e.g. `ctx.Result.Warnings.Add($"...")`)
  and strip them afterwards.

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

- **Numbered `Теми:` list reaching entry «10.»** — a list like `1.\tНазва (Прізвище)` … `12.\t…`
  where each entry carries a single surname. The `NextQuestionValueIs10` lookahead, run on entry
  `9.`, saw the *next* line `10.\tНе лише…` and read its `10.` prefix as a **value-10 question**, so
  it started a spurious 1-question theme, abandoned the list at entry 8 (`У списку «Теми:» 8 тем, а
  в пакеті знайдено 13`), and dumped entries 10-12 into question text. Fix: the list now follows its
  own `1..N` numbering (`ThemeListNextNumber` + `TryTakeSequentialListEntry`), so entry `10.` is
  recorded as list position 10, never a question. Symptom: an extra leading theme whose title is a
  later list entry and only holds a `10.` question with no answer.

- **Theme list printed without its `Теми:` header** — the package header just lists `1. Bezodnya
  Music` … `8. Швагер - ліга` and then repeats each entry above its theme. With no `Теми:` label the
  list was never read, so no anchors existed, and every theme fell through to the before-`10.`
  fallback — which then picked up the theme's *preamble* (`Цей канал присвячено українській музиці.`)
  as the title, or nothing at all when that preamble was too long. Fix: `LooksLikeImplicitThemeList`
  recognizes the list by its shape — an unbroken `1..N` run of short, title-like entries — and hands
  it to the normal anchor machinery. Contiguity is the discriminator: the real headers carry the same
  numbers but are separated by their questions. The entry cap (`MaxImplicitThemeListEntryLength`, 70)
  is what keeps numbered **host instructions** (`1. Ознайомитися з темами…`, `2. Читати усі
  коментарі…`) from being mistaken for a theme list — they are prose, and prose is long.
  Symptom: every theme titled with its own first descriptive sentence.

- **A numbered header repeating an unnumbered list entry** — the `Теми:` list is bare (`Рік Тигра`,
  `М.С.`) but the real headers are numbered (`1. Рік тигра`). `TryTakeSequentialListEntry` latched
  its `1..N` run onto that `1.` and filed the header as one more list entry. Fix: a numbered run may
  only start when no anchor has been recorded yet — a numbered list is numbered from its first entry.

- **Header states its own position** — `5. Я І МОЇ КОЗИ` opening the fifth theme, with a preamble
  between it and `10.`. `TryMatchPositionNumberedHeader` accepts it purely on the number matching
  the next theme's position, so it works with no theme list at all. It is deliberately narrow
  (previous theme complete, not part of a numbered run, next question in the document is a `10.`)
  because a numbered line is just as often an enumeration inside a comment.

- **The tenth theme's numbered header reads as a value-10 question** — `10. За (Костянтин Каунін)`
  opening theme 10 is shape-identical to a `10.` question, so it was parsed as one: a spurious
  1-question theme holding the *title* as its question text, then the real five questions under an
  untitled theme, and every later theme shifted by one. Positions 10/20/… are the only ones that can
  collide (a value must be a multiple of 10), which is why themes 1-9 were unaffected. Fix:
  `TryMatchPositionNumberedHeader` no longer bails on `IsQuestionStart` — its existing guards already
  discriminate, the decisive one being the lookahead (`NextQuestionStartValueIs10`): a theme's own
  first question is followed by its `20.`, a header by the `10.` it introduces. Its result also
  gates the list-number strip in `TryProcessThemeStart`, so the anchor rules see the bare title.
  Symptom: an extra theme whose single `10.` question text is literally the next theme's title,
  plus `У списку «Теми:» N тем, а в пакеті знайдено N+1`.

- **Editor line lost to a mistyped bracket** — `Редактор та автор тем: Едуард Голуб (Київ}` closes the
  city with `}`. `UkrainianNameHelper.StripCity` only recognized `(…)`, so the city stayed glued to the
  name, `LooksLikeAuthorList` saw a third "word" starting with `(` and rejected it, and the **whole
  editor line was silently dropped** — the package landed with no editor and, through the author
  cascade, no author on any of its 40 questions. `CityInParentheses` now also accepts `}` / `]` and a
  missing closer before end of string. Symptom to recognize: an editor line visibly present in the
  DOCX, absent from the import, and every question author-less. Note the failure is *silent* — the
  line just falls through to the preamble, which is also the correct behaviour for acknowledgment
  lines («Редактор дякує за тестування: …»), so there is no warning to look for.

- **Label wrapped in its own decoration** — two labels routinely arrive decorated, and the decoration
  used to leak into the stored field. `(Форма: ЦЯ ТВАРИНКА)` matched no label at line start, so only
  the *inline* rule found it: `(` was appended to the question text and the form kept the closing `)`.
  `Преамбула: у відповіді два слова…` kept its label inside the preamble text. Both are now stripped
  (`ParenthesizedFormLabel`, `ShvagerPreambleLabel`). The separator after the keyword is the
  discriminator in both cases — it is what keeps the host-instruction prose `(Форма запитання вказана
  після запитання в дужках курсивом)` and the header heading `Преамбула від редактора:` intact.

- **Title decorated differently in the list and in the header** — `"БАНДУРИСТ".` vs the list's
  `Бандурист`. `NormalizeTitle` strips quotes and terminal punctuation from both ends before
  matching, so the two forms meet.

- **Preamble prose that reads like a header** — `Тема - набірна матриця. Всі відповіді складаються
  із літер у слові БАНДУРИСТ…` matches `ShvagerThemeStart` and started a second theme whose title was
  the whole sentence. Fix: an explicit `Тема…` header is ignored while the current theme is still
  empty (`IsInsideEmptyTheme`) — a real header never directly follows another header.

- **Theme title separated from `10.` by a preamble** — a header shaped `Назва` / `Автор: …` /
  *preamble line* / (blank) / `10.…`. `NextQuestionValueIs10` skips the author line but not the
  preamble, so the title was lost (empty theme; the title leaked into the previous question's
  comment). Fix: anchors are additionally indexed by their **core** title — the list entry's trailing
  `(Прізвище)` stripped (`AnchorCores` + `TryConsumeAnchorByCore`) — so a bare header matches its
  `Теми:` entry directly, independent of the before-`10.` lookahead. This is the counterpart to the
  single-surname note above: the surname stays glued to the anchor, but the *core* still matches.

## Tests

Parser tests use synthetic blocks built with the `Block("...")` helper in
`QuestionsHub.UnitTests/ImportParsing/Parsing/PackageParserTests.cs`. Prefer reproducing a bug
as a synthetic-block test rather than depending on a real DOCX.

```bash
dotnet test QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj --filter "FullyQualifiedName~PackageParserTests"
```

Run from the repo root; quote project paths in PowerShell.
