# Backlog

Planned features and fixes in one place. Each item points to the doc that holds its design notes or context. Last reviewed 2026-10-10.

Security findings are **not** listed here: this repository is public, so open vulnerabilities are tracked privately until they are fixed.

## Features

1. **Package export and import through the agent API / MCP** — the next feature.
   - `.qhub` download with a token, for a local backup job of all packages.
   - Import through MCP: the user's agent converts a local DOCX to `.qhub` following the spec, and the package is uploaded as a Draft.
   - Design sketch: `AGENT_API_PLAN.md` → "Next feature".
2. **Agent API follow-ups** (`AGENT_API_PLAN.md` → "Deferred / follow-ups")
   - Revert a changeset. The audit already stores before/after values and snapshots of deleted entities.
   - Media upload/delete through the API.
   - Orphan author/tag cleanup after changesets.
   - Per-entity REST endpoints, if a UI client ever needs them.
3. **Results and statistics** (`Stats_plan.md`, `RESULTS.md` → "Deferred")
   - Своя гра results.
   - Statistics in the public API v1.
   - Results in the `.qhub` export.
   - Per-question "which teams answered" view. The data is already stored in `TeamResult.ResultsByQuestionJson`.
   - Auto-polling of embargoed tournaments.
4. **Своя гра** (`SHVAGER_PLAN.md`)
   - Full-text search over theme titles and Форма (needs a denormalized column).
   - Play mode.
   - Remember the home-tab choice on the client.
   - One representation of the game type in the public API: the list returns a numeric `type`, the detail a string `gameType`.
5. **Import pipeline** (`PACKAGE_IMPORT.md` → "Future Improvements")
   - DOC support via conversion (Gotenberg/LibreOffice).
   - PDF with OCR.
   - Retry failed jobs from the UI.
   - Scheduled cleanup of job folders.
6. **Accounts** (`AUTHENTICATION.md`)
   - Manual admin approval of registrations.
   - Ideas, not committed to: 2FA, account deletion (GDPR), social login.
7. **Media** (`MEDIA_SETUP.md`)
   - Thumbnails.
   - A cleanup tool for orphaned media.
   - CDN.

## Fixes and technical debt

- **Concurrency for the editor's own saves** (e.g. `xmin` on `Question`). An editor tab left open while an agent works can overwrite the agent's changes. The risk is accepted for now and the history panel warns about it (`AGENT_API_PLAN.md` → "Risks & Notes").
- **One implementation of the save rules.** Move `ManagePackageDetail.razor` onto the changeset engine; today the engine re-implements the editor's rules.
- **Displayed times.** Use one site-wide time zone instead of server-local `ToLocalTime()`.
- **Import config.** Make `PackageImport:AllowedExtensions` explicit. `appsettings.json` lists only `.docx`, but the configuration binder appends it to the code default, so `.qhub` is in fact allowed.
- **Shared media files.** Questions can share one media file (an asset referenced by several questions of an imported package). Removing or replacing it on one question in the editor deletes the file for all of them. Check references before deleting.
- **Login dropdown.** The guest dropdown (Увійти / Реєстрація) has an extra gap on the left, and the selection background overflows to the right. Reported 2026-03, not re-checked.
- **Stale docs.** `AUTHENTICATION.md` → "Future Enhancements" still lists access levels and email sending as planned; both are implemented.

## Pending manual steps

- After the agent API is merged and deployed:
  - Deploy `infra/nginx/questions.com.ua.conf` on the VPS by hand.
  - Check what the `TagsCaseInsensitiveUnique` migration merged.
  - Follow the checklist in `AGENT_API_PLAN.md` → "Deployment checklist".
