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
  - A one-off `CS2012: Cannot open '…\obj\Debug\net10.0\QuestionsHub.Blazor.dll' for writing`
    while the app is running is transient, so just rerun the same command. It is a brief clash
    with another build, not a lock to work around (2026-10-02: it passed on the next try, with the
    app still running).
- **Cleanest fallback: a detached git worktree** — fully isolates `bin/` *and* `obj/`, so it always
  works regardless of what's locked. Commit first (a worktree checks out a committed state):
  ```bash
  git worktree add --detach /tmp/qh-wt HEAD
  dotnet test /tmp/qh-wt/QuestionsHub.UnitTests/QuestionsHub.UnitTests.csproj --filter ...
  git worktree remove /tmp/qh-wt --force
  ```
  Non-disruptive: the user's running app / debug session is untouched.

## Verifying a UI change when the user's app is already running

The already-running app was started **before** your commits and does **not** hot-reload `.razor`/`.cs`
changes — driving `:5018` shows the *stale* UI/service, so an admin card or new endpoint you just
added simply won't be there. Don't kill their app. Instead run a **second live instance** from a
detached worktree on a **separate port**, against the **same dev Postgres** (Development connection
string → same DB, so seeded/test rows are visible to both):

```bash
git worktree add --detach /tmp/qh-verify HEAD          # commit first; worktree checks out HEAD
mkdir -p /tmp/qh-verify/uploads/handouts               # REQUIRED — see gotcha below
cd /tmp/qh-verify/QuestionsHub.Blazor
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5019 \
  dotnet run --no-launch-profile > /tmp/qh-verify.log 2>&1 &
# then drive http://127.0.0.1:5019 in a browser
```

- **Startup gotcha:** `Program.cs` `ConfigureMediaFileServing` **throws** `Handouts folder not found
  at '<root>/uploads/handouts'` if that dir is missing. `uploads/` lives outside the repo (gitignored,
  bind-mounted in real dev) so a fresh worktree lacks it → `mkdir -p uploads/handouts` in the worktree
  root first, or the app dies on boot.
- Different port = different origin = **separate auth cookie**; log in again on `:5019`.
- Cleanup: kill only the `:5019` PID (`netstat -ano | grep 127.0.0.1:5019` → `taskkill //PID <pid> //F`;
  never the user's `:5018`), then `git worktree remove /tmp/qh-verify --force`.
- The interactive admin merge uses a native `confirm()` — browser automation freezes on it. Override
  `window.confirm = () => true` via the JS tool *before* clicking, so the Blazor `JS.InvokeAsync<bool>("confirm", …)` returns true and proceeds.

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

## Signing in from curl (dev)

Captured 2026-10-11 (favorites end-to-end check).

- **The dev admin's password does not match** `AdminCredentials` in `appsettings.Development.json`:
  the admin row was seeded earlier with other credentials, so the login returns `error=invalid`.
  Don't fall back to the `.env` admin credentials, which may be real ones. **Register a throwaway user
  instead:** in Development with no `Email:ApiKey`, `POST /api/Auth/register` auto-confirms the user
  and signs them in.
  ```bash
  tok() { grep -o 'name="__RequestVerificationToken" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//'; }
  RT=$(curl -s -c jar -b jar http://localhost:5018/Account/Register | tok)   # HTML-unescape it if it contains &amp;
  curl -s -o /dev/null -c jar -b jar -w '%{http_code} %{redirect_url}\n' \
    --data-urlencode firstName=Тест --data-urlencode lastName=Тест --data-urlencode "email=$EMAIL" \
    --data-urlencode "password=$PASS" --data-urlencode "confirmPassword=$PASS" \
    --data-urlencode "__RequestVerificationToken=$RT" http://localhost:5018/api/Auth/register   # 302 → / = signed in
  ```
  Delete the user afterwards (its rows cascade):
  `DELETE FROM "AspNetUsers" WHERE "Email" LIKE '<prefix>%';`
- **The auth cookie is `Secure`**, so over `http://localhost:5018` curl stores it in the jar but never
  sends it back. Build the header yourself:
  `C=$(awk '!/^#/ || /^#HttpOnly_/ {sub(/^#HttpOnly_/,""); if (NF>=7) printf "%s=%s; ", $6, $7}' jar)`,
  then `curl -H "Cookie: $C" …`.
- **JSON endpoints called by page JS** (e.g. `/api/favorites`) need the antiforgery token as the
  `RequestVerificationToken` header. Scrape it from any page you load while signed in; the logout form
  in the layout carries one.
- Claude-in-Chrome cannot open `localhost` on this machine ("blocked by your organization's policy"),
  so curl is the way to check the UI headlessly.

## Integration tests

- They **skip silently** (they don't fail) when Docker isn't running. Docker Desktop is often stopped on
  this PC: start it, and wait until `docker info` answers before trusting a green run. The dev DB
  container `questions-hub-db` comes up with it.
- **Services that call `AccessControlService`'s parameterless methods** (e.g.
  `PackageService.DeletePackage` → `CanDeletePackage`) throw outside a Razor component ("Do not call
  GetAuthenticationStateAsync outside of the DI scope for a Razor component"). In a test, reproduce
  their EF operation directly (e.g. load tours + questions tracked, `Remove`, `SaveChanges`), or call
  services that take a `PackageAccessContext` / `ClaimsPrincipal`.
- Cookie-auth HTTP tests: `TestBrowser.Login` + `TestBrowser.GetAntiforgeryToken(browser, url)`.

## Realistic query plans on the dev DB

To check that a query or cascade stays index-driven at volume, bulk-insert rows inside a transaction
that is rolled back. The dev DB is left unchanged:

```bash
docker exec -i questions-hub-db psql -U questionshub -d questionshub <<'EOF'
BEGIN;
INSERT INTO "Favorites" ("UserId","PackageId","QuestionId","CreatedAt")
SELECT u."Id", t."PackageId", q."Id", now()
FROM "AspNetUsers" u CROSS JOIN "Questions" q JOIN "Tours" t ON t."Id" = q."TourId";
ANALYZE "Favorites";
EXPLAIN (ANALYZE, COSTS OFF, SUMMARY ON) DELETE FROM "Packages" WHERE "Id" = 48;  -- shows per-FK cascade trigger times
ROLLBACK;
EOF
```

With only a handful of rows Postgres picks sequential scans whatever the indexes, so tiny tables prove
nothing about indexes.

## Import job artifacts

Uploaded packages leave replayable artifacts under `uploads/jobs/{jobId}/` at the **repo
root** (`MediaUpload.UploadsPath`): `input/` (original file), `working/extracted.json`
(parser input), `output/package_import.json` (parser output). Replaying the parser against
a real failed import is documented in [IMPORT_DEBUGGING.md](IMPORT_DEBUGGING.md).
