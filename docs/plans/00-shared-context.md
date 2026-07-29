# Shared context — read first, every session

You are working in **LoanApi**, an ASP.NET Core **net10.0** Clean Architecture / CQRS
boilerplate. The loan domain is *sample* content; the value of the repo is the structure.

## Layer map

Project references flow inward: `WebApi → Infrastructure → Application → Domain`.
Note `Infrastructure` references `Application` (not the reverse).

| Assembly | Holds |
|---|---|
| `Domain` | Entities with private setters + static factories, domain events, `Domain/Repositories` (repo + unit-of-work abstractions), `Domain/Exceptions/DomainValidationException` |
| `Application` | CQRS handlers (command + handler in one file), FluentValidation validators, MediatR pipeline behaviours, DTOs, AutoMapper profiles, `Application/Common/Interfaces` (app abstractions: `IApplicationDbContext`, `ICurrentUserService`, `IDateTime`, `IIdentityService`, `IOtpService`, `IOtpCodeHasher`, `ISmsSender`) |
| `Infrastructure` | `ApplicationDbContext` (EF Core, SQL Server), `Persistence/Configurations` (`IEntityTypeConfiguration`), `Persistence/Repositories`, `Migrations`, ASP.NET Identity, `Services/*` |
| `WebApi` | Controllers, middleware, Serilog composition, `CurrentUserService` |

Each layer exposes one `ConfigureServices` extension — `AddApplicationServices`,
`AddInfrastructureServices`, `AddWebUIServices` — composed in `WebApi/Program.cs`.

## Build and test

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

`-p:SkipNSwag=True` is **required**. A Debug build of `WebApi` otherwise runs NSwag as a
post-build step, which boots the app's OpenAPI provider and needs the NSwag tool restorable.

EF migrations — `Infrastructure` holds them, `WebApi` is the startup project (PowerShell):

```powershell
$env:SkipNSwag = "True"
dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
```

Two expected, harmless noises from `dotnet ef`: a tools/runtime version warning (installed
`dotnet-ef` is 9.0.7 against EF Core 10.0.10 — it works, it nags), and a `HostAbortedException`
stack trace, which is how the tooling stops the host after building the service provider.

Migrations are applied at startup **in Development only** (`Program.cs`), gated on
`Database.IsSqlServer()`. `dotnet run` therefore needs SQL Server at `localhost\SQLEXPRESS`.
To work without one, set `"UseInMemoryDatabase": true` in `WebApi/appsettings.json`.

## Conventions that must not be broken

- **DI is convention-based.** Handlers, validators and `IEntityTypeConfiguration`s are
  discovered by assembly scanning (`AddMediatR`, `AddValidatorsFromAssembly`,
  `ApplyConfigurationsFromAssembly`). **Never hand-wire them.**
- **Entities are not anemic.** Private setters; construction through a static `Create` factory
  that enforces invariants and throws `DomainValidationException` with a **localization key**
  (not a sentence); mutation through named methods that raise domain events.
- **Domain events** derive from `BaseEvent : INotification` and are dispatched by
  `MediatorExtensions.DispatchDomainEvents`, called from `ApplicationDbContext.SaveChangesAsync`
  **before** `base.SaveChangesAsync`. Handlers therefore observe uncommitted state, and there is
  no outbox. Do not add side effects that must not happen on a failed save.
- **Localization.** `DomainValidationException` messages are keys resolved against
  `WebApi/Resources/localization.json`. Any new key must be added there.
- **The two-step OTP gate** is opt-in: a command implements `IRequireOtpVerification` and
  `OtpVerificationBehavior` does the rest. Do not add OTP logic to handlers.

## Documentation that mirrors the code

These describe conventions to future agent sessions. When a phase changes a convention, it
**must** update the relevant ones — the phase file says which.

- `.cursor/rules/00-project-core.mdc` — layer identity, where abstractions live
- `.cursor/rules/application-cqrs.mdc` — handler shape
- `.cursor/rules/domain-entities.mdc` — entity/factory/event rules
- `.cursor/rules/infrastructure-ef.mdc` — DbContext, configurations, repositories
- `.cursor/rules/testing.mdc` — what to mock
- `.cursor/rules/otp*.mdc`, `auth-identity*.mdc`, `logging*.mdc`
- `.cursor/skills/add-vertical-slice/{SKILL.md,reference.md}` — the end-to-end recipe for a new
  aggregate; **the single most important file to keep accurate**, because it is what generates
  new code
- `docs/architecture.md` — the deep reference
- `AGENTS.md`, `CLAUDE.md` — thin entrypoints, rarely need changing

## Current known state (as of plan authoring, 2026-07-30)

Branch `feature/master/TwoStepOTPVerification`, clean tree. Test projects:
`Domain.UnitTests` (NUnit, entity invariants) and `Application.UnitTests` (NUnit + Moq,
handlers/behaviours/`OtpService`). There are **no integration tests** — nothing exercises a
real database. Several defects in the phase files are invisible to the current suite for that
reason; where a phase fixes one, it adds the test that would have caught it.

## Reporting

Finish each phase with a short report: what changed, the build/test result, anything you found
that the plan did not anticipate, and anything you deliberately left out. If the plan is wrong
about the code, say so plainly rather than working around it silently.
