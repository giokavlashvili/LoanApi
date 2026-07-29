# Agent guide — Clean Architecture CQRS boilerplate

ASP.NET Core **net10.0** Web API template. Assemblies: `Domain`, `Application`, `Infrastructure`, `WebApi`. Loan domain is **sample** — replace when starting a product.

## Where guidance lives

| Need | Source |
|------|--------|
| Always-on identity + build | `.cursor/rules/00-project-core.mdc`, `01-build-and-run.mdc` |
| Layer conventions | `.cursor/rules/*` (domain, application, infrastructure, webapi, …) |
| Workflows | `.cursor/skills/` |
| Full deep reference | [`docs/architecture.md`](docs/architecture.md) |
| Thin Claude entrypoint | `CLAUDE.md` (points here / docs — not the full guide) |

## Slash commands

| Command | Does |
|---------|------|
| `/build` | `dotnet build … -p:SkipNSwag=True` |
| `/test` | `dotnet test … -p:SkipNSwag=True` |
| `/add-feature` | Run **add-vertical-slice** skill |
| `/migrate` | EF migration via **ef-migration** skill |
| `/repurpose` | Run **repurpose-boilerplate** skill (explicit only) |

## Skills

- `add-vertical-slice` — new aggregate/feature end-to-end (auto-invoke)
- `add-otp-gate` — OTP on a command (auto-invoke)
- `ef-migration` — SkipNSwag env + `dotnet ef` (auto-invoke)
- `repurpose-boilerplate` — strip sample domain (**`/repurpose` only**)

## Quick commands

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
```

Prefer **GitNexus** for explore / impact / refactor. Re-analyze when stale: `npx gitnexus analyze`.

Do not invent parallel folder layouts or manually wire DI for handlers/validators/EF configs.
