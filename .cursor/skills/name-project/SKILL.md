---
name: name-project
description: >-
  Renames product identity from LoanApi to a new Name (solution file, docs,
  connection catalog, docker password) without touching sample Loan* domain
  types or assembly namespaces. Use when running /name-project or renaming
  the template for a fresh product copy.
disable-model-invocation: true
---

# Name project

`$ARGUMENTS` is **Raw**. If missing/invalid → stop and ask. Do not guess.

## Derive names

1. `Raw` = trim `$ARGUMENTS`; strip trailing `.sln` if present.
2. Validate: non-empty, PascalCase, no spaces.
3. `Short` = `Raw` without trailing `Api` (`GeoWorkersApi` → `GeoWorkers`).
4. `Name` = `Short` + `Api` (`GeoWorkers` → `GeoWorkersApi`).
5. SQL catalog = `Short` (not `ShortDB`, not `Name`).

| Token | Becomes | Example (`/name-project GeoWorkers`) |
|---|---|---|
| `LoanApi` (exact) | `Name` | `GeoWorkersApi` |
| `LoanApi.sln` | `Name.sln` | `GeoWorkersApi.sln` |
| `LoanApi_Dev_Passw0rd!` | `{Short}_Dev_Passw0rd!` | `GeoWorkers_Dev_Passw0rd!` |
| `LoanDB` (SQL catalog) | `Short` | `GeoWorkers` |
| Assemblies `Domain` / `Application` / `Infrastructure` / `WebApi` | **unchanged** | |

**Hard rules**

- After edits, every `dotnet build|test … .sln` line must use `Name.sln` (never `Short.sln`).
- Truncated replace `LoanApi` → `Short` is the failure mode — always use `Name`.
- Password is **not** a blind `LoanApi` replace (that yields `{Name}_Dev_Passw0rd!`).
- Do **not** put `Name`/`Short` into C# namespaces or `.csproj` assembly names.

## Keep (do not rename / delete / “clean”)

Sample vertical slice stays:

- `Domain/Entities/{LoanApplication,LoanType}`, `LoanStatus`, events/repos
- `Application/LoanApplications/**`, `Application/LoanTypes/**`
- `WebApi/Controllers/{LoanApplications,LoanTypes}Controller`
- EF configs, `LoanApplicationRepository`, tables `LoanApplications`/`LoanTypes`, migrations
- `IApplicationDbContext.LoanApplications` / `LoanTypes`
- `VerifiableOperationType.DeleteLoanApplication`
- `InvalidLoanType` in `localization.json`
- Tests under `*LoanApplications*` / `LoanApplicationTests`
- Skills/rules that say “mirror `LoanApplication`”

## Change

### 1. `LoanApi` → `Name` (product token)

Scan whole repo (`.sln`, `.md`, `.mdc`, `.yml`, `.json`, docker-compose, Cursor commands/rules/skills, `AGENTS.md`, `CLAUDE.md`, `docs/architecture.md`). Rename `LoanApi.sln` → `Name.sln` on disk.

Typical hits: `.cursor/commands/{build,test,add-feature}.md`, `.cursor/rules/01-build-and-run.mdc`, `AGENTS.md`, `CLAUDE.md`, `docs/architecture.md`.

Fix identity lines (`"LoanApi" is only the .sln / folder name` → `Name`).

Do this **before** the password step, then fix password to `{Short}_Dev_Passw0rd!`.

### 2. `LoanDB` → `Short`

- `WebApi/appsettings.json` `ConnectionStrings:DefaultConnection`
- `docker-compose.override.yml`
- `Infrastructure.UnitTests/TestDb.cs` (must match appsettings)
- `docs/architecture.md` if it documents `Database=LoanDB`

Do not rename SQL tables `LoanApplications` / `LoanTypes`.

### 3. Identity phrasing

Replace “the loan domain is sample / replace when starting a product” with “sample domain / worked example.” Keep type names when pointing at the example.

Files: `AGENTS.md`, `CLAUDE.md`, `.cursor/rules/00-project-core.mdc`, `docs/architecture.md` intro, `.cursor/commands/repurpose.md`, `.cursor/skills/repurpose-boilerplate/SKILL.md`.

### 4. Skeleton “loan” metaphors (not sample feature)

| File | Change |
|---|---|
| `Domain/Entities/OtpVerification.cs` | “replayed against a loan approval” → “a different operation” |
| `Application/Otp/Services/OtpService.cs` | “spendable on a loan approval” → “a different purpose” |
| `Application.UnitTests/Otp/OtpServiceTests.cs` | same |
| `AuditableEntityInterceptor.cs` | not `LoanApplication.Update` as framework — “an auditable aggregate’s Update” |
| Generic fixtures (NOT `LoanApplications/` tests) | `ApproveLoanOperation` → `ApproveSampleOperation`; `CloseLoanOperation` → `CloseSampleOperation`; `CanApproveLoans` → `CanApprove`; `ApproveLoanPayload`/`LoanId`/`loanId` → `ApproveSamplePayload`/`EntityId`/`entityId`; `PendingOperationTests` `"ApproveLoan"` / `{"loanId":42}` → `"ApproveOperation"` / `{"entityId":42}` |

Keep `VerifiableOperationType.DeleteLoanApplication`. Leave comments that only cite the example type (`SlugifyParameterTransformer` → `LoanApplicationsController`, etc.).

### 5. Historical `docs/plans/*`

Mechanical `LoanApi.sln` → `Name.sln` only. Do not rewrite plan prose about `LoanApplication`. If skipped, say so in the summary.

## Procedure

```
Progress:
- [ ] 1. Validate Raw; derive Short, Name, catalog
- [ ] 2. Confirm LoanApi.sln exists (or already-renamed → stop)
- [ ] 3. Grep: LoanApi, LoanDB, loan domain, LoanApi_Dev_Passw0rd, Database=LoanDB
- [ ] 4. Apply keep/change; rename .sln → Name.sln
- [ ] 5. Grep verify (below)
- [ ] 6. dotnet build Name.sln -p:SkipNSwag=True; report leftovers
```

### Post-edit grep must show

- Zero `LoanApi`
- Zero `LoanDB`
- Zero `Short.sln` truncated refs — only `Name.sln`
- Password = `{Short}_Dev_Passw0rd!` (not `{Name}_Dev_Passw0rd!`)
- Remaining `Loan*` = sample slice + “mirror LoanApplication” pointers only

## Do not

- Commit unless asked
- Run `/repurpose`
- Delete sample controllers or entities
