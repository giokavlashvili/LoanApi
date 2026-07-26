# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

A **Clean Architecture / CQRS boilerplate** for ASP.NET Core (net10.0) Web APIs. It is copied as the starting point for new projects, so the loan domain (`LoanApplication`, `LoanType`, `Currency`, `LoanStatus`) is **sample content demonstrating the patterns**, not the point of the repo. Expect it to be renamed and its domain replaced.

The project name is not baked into the code: assemblies/namespaces are the generic `Domain`, `Application`, `Infrastructure`, `WebApi`, and the string "LoanApi" appears only in the solution *filename* and repo folder name. When repurposing:

- Rename `LoanApi.sln` and the repo folder (solution *contents* need no edits).
- `WebApi/appsettings.json`: `ConnectionStrings:DefaultConnection` (`Database=LoanDB`) and `JWT:Secret`.
- Replace the sample vertical slices: `Domain/Entities`, `Domain/Events`, `Domain/Enums`, `Domain/Repositories`, `Application/LoanApplications`, `Application/Currencies`, `Application/LoanTypes`, `Infrastructure/Persistence/{Configurations,Repositories}`, `WebApi/Controllers/{LoanApplication,Currency,LoanType}Controller.cs`, the seed data in `ApplicationDbContextInitialiser.TrySeedAsync`, `WebApi/Resources/localization.json`, and the existing `Infrastructure/Migrations` (delete and re-create an Initial migration for a new domain).
- Keep: `Domain/Common`, `Application/Common`, `Application/Authenticate`, `Infrastructure/Identity`, `WebApi/{Filters,Middlwares,Extensions,Localization,Services}` — that is the reusable skeleton.

## Commands

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Run a single test / fixture (NUnit + `dotnet test` filter):

```bash
dotnet test Domain.UnitTests/Domain.UnitTests.csproj --filter "FullyQualifiedName~LoanApplicationTests.UpdateLoanApplicationStatus_WhenCalled_ChangeStatus"
```

Run the API (Swagger UI at `/swagger`; http profile = 5041, https = 7233):

```bash
dotnet run --project WebApi/WebApi.csproj --launch-profile https
```

EF Core migrations — `Infrastructure` holds the migrations, `WebApi` is the startup project:

```bash
dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
```

```bash
dotnet ef database update --project Infrastructure --startup-project WebApi
```

Notes on the build:

- A **Debug build of `WebApi` runs NSwag as a post-build step** (`NSwag` target in `WebApi/WebApi.csproj`): it generates `WebApi/wwwroot/api/specification.json` and the Angular client `WebApi/ApiClient/web-api-client.ts`. It boots the app's OpenAPI document provider, so a DI/startup regression fails the *build*, not just runtime. It does not need a database. Pass `-p:SkipNSwag=True` to skip it (faster, and required if the NSwag tool isn't restorable).
- In Development, startup applies migrations and seeds (`Program.cs`), so `dotnet run` needs SQL Server at `localhost\SQLEXPRESS`. To run without one, set `"UseInMemoryDatabase": true` in `WebApi/appsettings.json` (switch honored in `Infrastructure/Common/Extensions/ConfigureServices.cs`).
- Seeded dev credentials: `administrator@localhost` / `Administrator1!` (role `Administrator`).

## Architecture

Project references flow inward: `WebApi → Infrastructure → Application → Domain`. Note `Infrastructure` references `Application` (not the reverse) — abstractions such as `IApplicationDbContext`, `ICurrentUserService`, `IDateTime`, `IIdentityService` live in `Application/Common/Interfaces`, while repository/unit-of-work abstractions live in `Domain/Repositories`. Each layer exposes one `ConfigureServices` extension (`AddApplicationServices`, `AddInfrastructureServices`, `AddWebUIServices`) composed in `WebApi/Program.cs`.

### Request flow

Controller (thin, `ApiControllerBase.Mediator.Send`) → `ValidationBehavior` → `PerformanceBehavior` → handler → `IUnitOfWork` repository → `ApplicationDbContext.SaveChangesAsync` → domain events dispatched → response; exceptions shaped by `ApiExceptionFilterAttribute`.

### Conventions that matter when adding code

- **Vertical slices in `Application`**: `Application/<Feature>/{Commands,Queries,Dtos,Validators,EventHandlers}`. The MediatR request *and* its handler live in the same file (e.g. `CreateApplicationCommand.cs` holds `CreateApplicationCommand` + `CreateApplicationCommandhandler`). Commands/queries are `record`s implementing `IRequest`/`IRequest<T>`.
- **Registration is assembly-scan based** — validators, MediatR handlers, AutoMapper profiles, and EF `IEntityTypeConfiguration`s are all discovered automatically. New files in the right place need no DI wiring.
- **DTOs map via `IMapFrom<T>`** (`Application/Common/Mappings`): implement the interface for the default map, or override `Mapping(Profile)` for custom members. `MappingProfile` reflects over the Application assembly.
- **Business rules live in the domain entity**, not the handler. Entities have `private set` properties, a `static Create(...)` factory and mutator methods that throw `DomainValidationException` and call `AddDomainEvent(...)` (see `Domain/Entities/LoanApplication.cs`). Handlers only orchestrate: resolve current user/time, call the entity, save.
- **FluentValidation validators handle input/lookup validation** (e.g. "does this CurrencyId exist"); they are injected with `IUnitOfWork` and `IStringLocalizer` and are run by `ValidationBehavior` before the handler.
- **Domain events**: entities queue events; `ApplicationDbContext.SaveChangesAsync` calls `MediatorExtensions.DispatchDomainEvents` *before* persisting, publishing them to `INotificationHandler`s in `Application/<Feature>/EventHandlers`.
- **Repositories/UnitOfWork**: generic `Repository<TEntity>` plus per-aggregate repositories for query-specific methods (paging, includes). Adding an aggregate means: interface in `Domain/Repositories`, implementation in `Infrastructure/Persistence/Repositories`, and a property on both `IUnitOfWork` and `UnitOfWork`. `UnitOfWork`'s constructor takes the repositories as *optional* parameters purely so unit tests can inject mocks (`new Mock<UnitOfWork>(context, ..., repoMock.Object)`).
- **Controllers**: inherit `ApiControllerBase`, route `api/v1/[controller]` with `[Route(nameof(Action))]` per action, `[Authorize]` at class level for protected controllers. One line per action delegating to `Mediator`.

### Cross-cutting behavior

- **Errors → HTTP**: `WebApi/Filters/ApiExceptionFilterAttribute.cs` maps `ValidationException` (400 + `ValidationProblemDetails`), `NotFoundException` (404), `Unauthorized`/`ForbiddenAccessException`, and `DomainValidationExceptionWrapper` (400, message localized). `ValidationBehavior` is what wraps a raw `DomainValidationException` into `DomainValidationExceptionWrapper`. Anything unmapped is caught by `UnhandledExceptionHandlerMiddlware` and returned as a generic 500. **Domain exception messages are localization keys, not user text** — add the key to `WebApi/Resources/localization.json` (`ka-GE` / `en-US`).
- **Localization**: the `x-sys-language` request header sets the culture (`SysLanguageMiddleware`); `JsonStringLocalizer` (registered as a singleton `IStringLocalizer`) reads `Resources/localization.json` from disk at construction. The header is stripped from the generated TS client via `excludedParameterNames` in `nswag.json`.
- **Auth**: ASP.NET Identity (`ApplicationUser : IdentityUser` with FirstName/LastName/PersonalNumber/BirthDate) + JWT bearer. `IdentityService` both implements identity operations and mints tokens from `JWT:Secret` / `JWT:ExpireMinutes`. Issuer/audience validation is disabled by design in the template.
- **Logging**: NLog via `nlog.config` — file target under `WebApi/logs/`, plus a **database target writing warnings+ to the `Logs` table** using the connection string pushed into `GlobalDiagnosticsContext` by `AddNlog`. `LoggingMiddleware` logs each request/response; `PerformanceBehavior` warns on handlers slower than 500 ms.

## Known state / gotchas

- `WebApi/Dockerfile` still targets .NET 7 base/SDK images while the projects target net10.0 — update the tags before relying on `docker-compose`.
- Deliberate misspellings/inconsistencies are load-bearing for existing `using`s: the folder `WebApi/Middlwares`, namespaces `WebUI.Filters` / `WebUI.Services` inside the WebApi project, and `CreateApplicationCommandhandler` (lowercase `h`).
- `WebApi/Filters/SwaggerAttributes.cs` is excluded from compilation via `<Compile Remove>`.
- `WebApi/ClientApp` contains only build output and empty folders (no `package.json`, nothing tracked under `src`); the Angular app is not part of this template — only the generated `WebApi/ApiClient/web-api-client.ts` is.
- `dotnet build` emits NU1903 warnings for AutoMapper (pinned to 14.x/15.x to avoid the commercial license gate) and duplicate/prunable `PackageReference` warnings in `Infrastructure`/`WebApi`.
