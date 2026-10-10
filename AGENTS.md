# AGENTS.md — Questions Hub

Context for non-Claude coding agents (e.g. Codex) working in this repository.

- **Project conventions:** read `CLAUDE.md` first — it is the canonical guide (stack, layout, domain model, code conventions, git rules, key docs table). Everything there applies to you too.
- **Feature in progress:** `docs/AGENT_API_PLAN.md` — agent write API (personal access tokens, changesets, audit, MCP). It is the source of truth for design decisions and the phase checklist.
- **Build & test:** `dotnet build` and `dotnet test` from the repo root (Windows, .NET 10). Unit tests use the EF Core InMemory provider (`QuestionsHub.UnitTests/TestInfrastructure/InMemoryDbContextFactory.cs`).
- **Do not** commit, push, create branches or run migrations against any database unless the prompt explicitly asks. Never touch `.env`, `keys/`, `postgres_data/`, `uploads/`.
- Files are UTF-8 **without BOM**. UI strings are Ukrainian (uk-UA). No `Async` suffix on method names.
- **When reviewing:** report findings as a list ranked by severity, each with `file:line`, what is wrong, a concrete failure scenario, and a suggested fix. Distinguish real bugs from style nits. Say explicitly when you found nothing significant.
