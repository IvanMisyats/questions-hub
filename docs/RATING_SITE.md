# rating.chgk.info ("сайт рейтингу")

Working notes for tasks that involve the rating site: finding the tournaments a package was played at, attaching their results, filling play dates. How results are loaded and mapped onto questions is covered in [RESULTS.md](RESULTS.md). The Agent API that attaches them is in [API.md](API.md) ("Agent API"). This page covers the rating site itself and how to match its tournaments to our packages. Captured 2026-10-13 from a backfill of ~70 packages (results attached to every ЩДК package that had none).

## The site

- `https://rating.chgk.info` is the platform where most ЩДК tournaments are organized. The interface is in Russian. Most packages in our database were played at tournaments registered there.
- Tournament page: `https://rating.chgk.info/tournament/{id}` (also `/tournaments/{id}`). Our `ResultsUrlParser` accepts both, plus `…/{id}/…` sub-pages, the API URL and a bare id.
- **Tournament types** (`type.name`):
  - `Синхрон`: a short fixed window, usually Friday to the next Wednesday.
  - `Асинхрон`: usually right after the synchron, over a much longer window, often online.
  - `Обычный`: an in-person tournament (очний).
  - `Строго синхронный`: also occurs.

  The longer a tournament runs, the more likely the facts in its questions leak or collide with other tournaments.
- **One package is often several tournaments**: synchron + asynchron, and separate Ukrainian and Russian versions. Typical name suffixes: `(синхрон)`, `(а/о)` = асинхрон/онлайн, `українською`, `(укр)`, `(асинхрон и онлайн)`. Names are in the language the organizer used, often Russian translations of our Ukrainian titles.

## API (anonymous, read-only)

Base `https://api.rating.chgk.info`. No auth is needed for reads. The `.env` credentials are unused: a JWT does not bypass the results embargo.

| Request | Notes |
|---|---|
| `GET /tournaments?…` | Filters: `name`, `language` (e.g. `uk`), `editor` (player id), `type`, `dateStart[after\|before\|strictly_*]`, `dateEnd[…]`, `town`, `order[id]`. `itemsPerPage` max **512**, `page` 1-based. |
| `GET /tournaments/{id}` | `name`, `longName`, `type`, `languages`, `dateStart`/`dateEnd`, `editors` (id, name, patronymic, surname), `orgcommittee`, `questionQty` (per-tour counts, e.g. `{"1":12,"2":12,"3":12}`), `hideResultsTo` (embargo). |
| `GET /tournaments/{id}/results?includeMasksAndControversials=1` | Teams with `mask` (one char per question: `1`/`0`/`?`/`X`). Masks are `null` until `hideResultsTo`. |
| `GET /players?surname=…`, `GET /players/{id}/tournaments` | Players. Editors are matched by player id in the `editor` filter. |
| `GET /docs` with `Accept: application/vnd.openapi+json` | Full OpenAPI spec (`/docs.json` returns 404). |

- `Accept: application/json` returns a plain array. `Accept: application/ld+json` returns a Hydra collection with `totalItems` and paging links.
- The list items carry editors and `teamsCount` but **not** `questionQty`; fetch the detail for that.
- Downloading every tournament since mid-2014 is ~10 250 items, 21 pages of 512, a few seconds. No rate limiting was observed. Pace detail calls a little anyway (≈0.3 s).
- **Times are UTC.** Organizers set local midnights, e.g. `2015-04-02T21:00Z` = 3 April 00:00 Moscow/Kyiv, and `…T20:55Z` = 23:55. Convert to **Kyiv time** before taking the date. The UTC date part is off by one for such tournaments.

## Finding the tournaments of a package

Use `tools/rating/find_tournaments.py <packageId>…` (reads the package through the Agent API; token in `QH_AGENT_TOKEN` or `--token-file`). It shortlists candidates and checks each one's question count. The method, if you do it by hand:

1. **Signals.**
   - **Name.** Fold Ukrainian and Russian letters (і/ї/й→и, є→е, ы→и, э→е, drop ь/ъ/apostrophes) and compare word overlap.
   - **Editors.** Compare surnames: the rating stores Russian transliterations (Ільїн → Ильин, Місяць → Мисяць, Вахрів → Вахрив, Дідич → Дидыч, Матвєєвський → Матвеевский), and folding makes most of them equal.
   - **Dates.** Proximity to the package's `playedFrom`.
2. **Verify the question count.** Compare the sum of `questionQty` with the package's regular-tour questions (warm-up and shoot-out excluded). This is the decisive check, because results map onto questions **by position** ([RESULTS.md](RESULTS.md)):
   - Total and tour split match: stats map.
   - Same total, different split (6×6 vs 3×12, 1×36 vs 3×12): stats map, with a warning.
   - Different total: standings only.
3. **Collect the siblings** of a confirmed tournament: tournaments with the **same editor set** starting within ~45 days. These are the other language versions and the synchron/asynchron pair. Attach **all** of them; the package's stats are summed over its sources (precedent: package 140 has the synchron, the Ukrainian asynchron and the Russian asynchron).
4. **Translated titles** that name matching misses. Search by editors and dates instead:
   - «Пир» = «Бенкет», «Пир (второй)» = «Бенкет (другий)»
   - «Угрюмый Ёрш-3» = «Похмурий Йорж 3»
   - «Кубок Слегка Несредиземья» = «Кубок Трішки Несередземʼя»
   - «Галера в закате» = «Галера надвечір»
   - «Восточный экспресс» = «Східний експрес»
   - «Буквы, в которые играют люди» = «Літери, в які грають люди»
   - «Александрийская трилогия» = «Олександрійська трилогія»
   - «Квестія» (2024-11) = «Квестія vol. 4»

### Pitfalls

- **The same name recurs every year** (Ам Исраэль Хай, Simply Red/Green/White, Кубок Лося, Кубок Слегка Несредиземья): pick by dates.
- **Partial mirrors**: «Зеркало … - 1» and «- 2», «Midsommer-1» and «-2», «Дзеркало фестивалю … Частина 1» each cover part of a bigger package. The count check fails, so these give standings only. Not attached.
- **«The Best» selections** (BERLINFEST. The Best, AmstelFest 2025. The Best, Финал Чемпионата Украины 2025 The Best, «… кубок ЛУК The Best») are packages of their own. They are not the festival or championship they were picked from.
- **Not on the rating**: since 2022 the ЛУК in-person cups and finals (Весняний/Літній Кубок ЛУК, Фінал сезону ЛУК), Національна ліга 2018, the school synchrons, and a few author packages. Don't force a match.
- **Extra questions in our package**: «0»-numbered questions inside regular tours, or replacement questions, break the positional count. Example: In a heartbeat has 40 questions vs 36 on the rating.
- **Editors missing** on some rating tournaments (e.g. Simply White asynchron): rely on name and dates.
- **`questionQty` can be wrong.** «Як не треба писати запитання V» (10635/10636) reports 1 question, but the team masks are 3 characters long. The loader skips such masks, so only the standings are kept. For a borderline match, check a mask length in `/results` too.

## Ivan's conventions

- **Show the mapping before writing anything**: package (title, link) → tournament(s) with link, editors, type and dates. Wait for confirmation.
- **Attach every version** of the package (synchron, asynchron, Ukrainian, Russian).
- **Play dates**: fill only the empty `playedFrom`/`playedTo`, with the minimum start and maximum end over the attached tournaments, as Kyiv dates. **Never change dates that are already set.** Write them through an Agent API changeset (`updatePackage`).
- Count-mismatch cases (tournament found, stats won't map) are listed for Ivan, not attached silently.
