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
- Assemblies: `Domain`, `Application`, `Infrastructure`, `WebApi`.
- Build/test: `dotnet build|test LoanApi.sln -p:SkipNSwag=True`
- EF (PowerShell): `$env:SkipNSwag = "True"; dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi`
- Prefer **GitNexus** for explore / impact / refactor.
