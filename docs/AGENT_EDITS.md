# Applying package corrections through the Agent API

Runbook for an agent (Claude Code, Codex) that applies an editor's corrections to production packages with a personal access token. The API contract is in `API.md` → "Agent API" (also served by the MCP tool `get_api_reference`). Captured 2026-10-10 from the first real corrections (authors of Своя гра themes in packages 239, 240 and 242).

## Workflow

1. **Token.** A `readWrite` token from the user (profile page → «Токени доступу для агентів»). Read it from wherever the user stored it into a variable; never print it, log it or write it into files, scripts or commits.
2. **Locate.** Site links carry the ids: `/package/{packageId}#tour-{tourId}` and `/package/{packageId}#question-{questionId}`. Read the package (`GET /api/v1/manage/packages/{id}` or MCP `get_package`). Check that the theme or question **titles match** what the user wrote before touching anything.
3. **People.** Look each person up by full name (`/authors?search=Ім'я Прізвище` — every word must start the first or last name). Reference existing people by id. If there is no exact match, also search by a prefix of the last name and by the first name. Only then let the changeset create the author by name; the preview warns `New author '…' will be created`. Report the new author's id to the user, in case a differently spelled duplicate shows up later.
4. **One changeset per package**, previewed (`dryRun: true`, `expectedVersion` = the package `version`). Compare the preview with the request: it must contain exactly the requested changes, with no unexpected fields and no unexpected warnings. Then apply with a fresh `requestId` and a Ukrainian `summary`.
5. **Verify.** Re-read the package. Check the public page: SSR HTML-encodes Cyrillic (see `LOCAL_DEV.md`), so grep for links (`editor/{authorId}"`), not for names.

A small script per task beats hand-written curl. Keep the operations in data (package, tour id, title as sent, author), with modes for inspect / preview / apply, and assert that the preview has no unexpected changes before applying.

## Ivan's conventions

- **Pasted question text only identifies the question.** Change only the fields that are named, e.g. authors. Never edit text, even when the stored text differs from what was pasted.
- **"Автор теми і всіх запитань: X"** = `setTourEditors` for the theme plus `setQuestionAuthors` for every question in it, all with `[X]`. Lists are replaced as a whole.
- **Package-level editors stay unchanged** when only some themes get a different author. With `sharedEditors: true` the package editors are the package's editor team. Individual theme or question authors are credited on their own themes and questions only (the same as in existing packages, e.g. 244).
- Ask only when the requested change itself is ambiguous.

## Gotchas

- **Cloudflare blocks Python's default User-Agent** (`Python-urllib/…` → `403`, error code 1010, Browser Integrity Check). Set any other User-Agent in Python scripts. curl, Node, httpx and reqwest (the MCP clients) pass.
- **Windows Python + Cyrillic output:** `python -I` ignores `PYTHONIOENCODING`, so `print` fails with `cp1252`. Run `python -I -X utf8`.
- **Structural changes are refused on packages with results** (`hasResults: true`). Author and text fixes still work; adding, deleting or moving questions has to be done in the editor.

## Testing changes to the MCP server

To check that `/mcp` still describes itself well enough, run a **fresh agent that is restricted to curl** against a local copy:

- **Local copy.** Run the app on `http://127.0.0.1:5019` against the dev database. Use `dotnet run --no-build --no-launch-profile`. The running app locks `bin/`, so build tests with `-p:BaseOutputPath=…` (see `LOCAL_DEV.md`).
- **Token.** Insert one straight into the dev database:
  - `TokenHash` = lowercase hex SHA-256 of the raw `qh_pat_` + 32 hex characters
  - `TokenPrefix` = its first 16 characters
  - `Scope` `1` = readWrite
  - `PackageIds` = `{id}`
  - `ExpiresAt` = tomorrow
- **The task.** Give the agent only the endpoint, the token (the **real** value — a placeholder wastes the run), the rule "no repo, no docs, no internet", and an editor-style correction list in Ukrainian. Ask for its call path and a critical assessment of the self-description.
- **Cleanup.** Save the package (`GET`) before the run, restore it afterwards through a changeset, delete authors the run created, and revoke the token.
