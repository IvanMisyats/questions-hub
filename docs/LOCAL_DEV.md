# Local Dev & Headless Verification

Facts for running and smoke-testing the app without an IDE (e.g. from an agent shell).
Captured 2026-07 during the Shvager feature work.

## Running the app headlessly

- `dotnet run` (from `QuestionsHub.Blazor/`) uses the **first launch profile → http://localhost:5018**.
  The `https://localhost:5001` address in CLAUDE.md applies to IDE runs; there are also
  `https://localhost:7290;http://localhost:5019` profiles and `--reset-db` variants in
  `Properties/launchSettings.json`.
- The app **auto-applies EF migrations on startup** (`Program.cs` → `MigrateAsync`) and seeds
  roles + sample packages when the database is empty.
- Run in the background with output to a file, then poll the port:
  ```bash
  ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build > /tmp/app.log 2>&1 &
  until curl -s -o /dev/null -w '%{http_code}' http://localhost:5018/ | grep -q 200; do sleep 2; done
  ```
  Do **not** pipe `dotnet run` through `head`/`grep` — the pipe closes after N lines and the
  log capture silently stops.
- **A running app locks `bin/`** — rebuilds fail with MSB3026/MSB3027. Stop it first:
  `taskkill //IM QuestionsHub.Blazor.exe //F` (Git Bash needs the doubled slashes).
  A running-under-**debugger** app locks both `QuestionsHub.Blazor.exe` *and*
  `QuestionsHub.Blazor.dll` (the error names the VS Debug Adapter as the holder).
- **Build/test WITHOUT stopping the app** by redirecting output away from the locked `bin/`:
  ```bash
  dotnet test QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj \
    -p:BaseOutputPath=/tmp/qh-testbin/ -p:UseAppHost=false
  ```
  `BaseOutputPath` is a global property, so it flows to every referenced project (the Blazor
  DLL included) and nothing is written to the real `bin/`. Must end with a slash. `UseAppHost=false`
  skips the apphost `.exe` copy (belt-and-suspenders). The test host runs from the scratch dir;
  the full suite passes there. Use this instead of killing a debug session you didn't start.
  - **Redirect `BaseOutputPath` only — do NOT also set `BaseIntermediateOutputPath`.** Redirecting
    `obj/` too makes the compiler see the generated `AssemblyInfo`/`AssemblyAttributes` twice and
    the build dies with `CS0579`/`CS0101` duplicate-attribute errors. `bin/` is what's locked; `obj/`
    isn't, so leave it at the default.
  - Often only the apphost **`.exe`** is locked (a plain `dotnet run`, not a debugger), not the
    `.dll` — in that case `-p:UseAppHost=false` **alone** lets the build/test succeed.
- **Cleanest fallback: a detached git worktree** — fully isolates `bin/` *and* `obj/`, so it always
  works regardless of what's locked. Commit first (a worktree checks out a committed state):
  ```bash
  git worktree add --detach /tmp/qh-wt HEAD
  dotnet test /tmp/qh-wt/QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj --filter ...
  git worktree remove /tmp/qh-wt --force
  ```
  Non-disruptive: the user's running app / debug session is untouched.

## Dev database without the PowerShell script

`start-dev-db.ps1` is just env vars + compose; the bash equivalent:

```bash
POSTGRES_HOST_AUTH_METHOD=trust POSTGRES_ROOT_PASSWORD=dev_root_password \
QUESTIONSHUB_PASSWORD=dev_password_123 docker compose --profile dev up -d
```

Direct SQL access (seeding test data, inspecting join tables):

```bash
docker exec -i questions-hub-db psql -U questionshub -d questionshub
```

Useful when hand-seeding: many-to-many join tables use EF conventional names/columns, e.g.
`"TourEditors"("EditorsId", "ToursId")`, `"QuestionAuthors"("AuthorsId", "QuestionsId")`.

## Smoke-testing rendered pages with curl

- **Percent-encode Cyrillic URL path segments.** `curl http://localhost:5018/search/жереб`
  sends raw bytes — the route param doesn't bind and the page renders as if no query was
  given (no error, easy to misread as "0 results"). Encode first:
  `/search/%D0%B6%D0%B5%D1%80%D0%B5%D0%B1`.
- **Blazor SSR output mixes two encodings.** Text from C# expressions (`@tour.Title`,
  `@SomeMethod()`) is HTML-entity-encoded (`&#x416;&#x435;...`), while literal Cyrillic in
  the `.razor` markup stays raw UTF-8. When grepping rendered HTML for markers, try both
  forms before concluding something is missing.
- With `[StreamRendering]`, the streamed HTML can contain **both** the initial and the
  updated frame of the same element (e.g. a count showing `0` then `1`) — match the last one.
- Scoped-CSS adds `b-xxxxxxxxxx` attributes to elements of components that have a `.razor.css`
  file — `grep '<strong>2</strong>'` misses `<strong b-xe4fulzb96>2</strong>`; use
  `<strong[^>]*>` patterns.

## Import job artifacts

Uploaded packages leave replayable artifacts under `uploads/jobs/{jobId}/` at the **repo
root** (`MediaUpload.UploadsPath`): `input/` (original file), `working/extracted.json`
(parser input), `output/package_import.json` (parser output). Replaying the parser against
a real failed import is documented in [IMPORT_DEBUGGING.md](IMPORT_DEBUGGING.md).
