# CLAUDE.md

Guidance entrypoint for Claude Code and other agents.

**Do not put the full architecture guide here** — Cursor (and similar tools) always-inject root `CLAUDE.md`, which doubles context when `.cursor/rules` already cover conventions.

| Need | Read |
|------|------|
| Day-to-day Cursor work | `.cursor/rules/`, `.cursor/skills/`, `.cursor/commands/` |
| Agent index | `AGENTS.md` |
| Full architecture / gotchas | [`docs/architecture.md`](docs/architecture.md) |

## Quick facts

- Clean Architecture / CQRS **boilerplate** (net10.0). Loan domain is **sample** — replace for real products.
- Assemblies: `Domain`, `Application`, `Infrastructure`, `WebApi`, `Infrastructure.Postgres` (PostgreSQL migrations only).
- Build/test: `dotnet build|test LoanApi.sln -p:SkipNSwag=True`
- DB provider: `Database:Provider` = `SqlServer` (default) | `Postgres` | `InMemory`. Drives EF **and** the Serilog log sink.
- EF (PowerShell): `$env:SkipNSwag = "True"; dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi`
- EF for PostgreSQL — **separate migration set, both flags needed**: `$env:Database__Provider = "Postgres"; dotnet ef migrations add <Name> --project Infrastructure.Postgres --startup-project WebApi`. A schema change needs both sets regenerated.
- Prefer **GitNexus** for explore / impact / refactor.
