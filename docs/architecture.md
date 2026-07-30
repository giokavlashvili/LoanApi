# Project architecture guide

Deep reference for this Clean Architecture / CQRS boilerplate. Agents should prefer `.cursor/rules/` and `.cursor/skills/` for day-to-day work; read this file when you need full detail.

## What this repository is

A **Clean Architecture / CQRS boilerplate** for ASP.NET Core (net10.0) Web APIs. It is copied as the starting point for new projects, so the loan domain (`LoanApplication`, `LoanType`, `Currency`, `LoanStatus`) is **sample content demonstrating the patterns**, not the point of the repo. Expect it to be renamed and its domain replaced.

The project name is not baked into the code: assemblies/namespaces are the generic `Domain`, `Application`, `Infrastructure`, `WebApi`, and the string "LoanApi" appears only in the solution *filename* and repo folder name. When repurposing:

- Rename `LoanApi.sln` and the repo folder (solution *contents* need no edits).
- `WebApi/appsettings.json`: `ConnectionStrings:DefaultConnection` (`Database=LoanDB`) and `JWT:Secret`.
- Replace the sample vertical slices: `Domain/Entities`, `Domain/Events`, `Domain/Enums`, `Domain/Repositories`, `Application/LoanApplications`, `Application/Currencies`, `Application/LoanTypes`, `Infrastructure/Persistence/{Configurations,Repositories}`, `WebApi/Controllers/{LoanApplication,Currency,LoanType}Controller.cs`, the seed data in `ApplicationDbContextInitialiser.TrySeedAsync`, `WebApi/Resources/localization.json`, and the existing `Infrastructure/Migrations` (delete and re-create an Initial migration for a new domain).
- Keep: `Domain/Common`, `Application/Common`, `Application/Authenticate`, `Infrastructure/Identity`, `WebApi/{Filters,Middlwares,Extensions,Localization,Services}` -- that is the reusable skeleton.

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

EF Core migrations -- `Infrastructure` holds the migrations, `WebApi` is the startup project:

```bash
SkipNSwag=True dotnet ef migrations add <Name> --project Infrastructure --startup-project WebApi
```

```bash
dotnet ef database update --project Infrastructure --startup-project WebApi
```

`SkipNSwag` has to be an **environment variable** here, not `-p:SkipNSwag=True`: `dotnet ef` forwards everything after `--` to the *application*, not to MSBuild, so the flag never reaches the build and NSwag runs anyway. PowerShell equivalent: `$env:SkipNSwag = "True"; dotnet ef migrations add ...`. Without it the scaffold build fails wherever the NSwag tool isn't restorable.

Two expected-and-harmless noises from `dotnet ef`: a tools/runtime version warning (the installed `dotnet-ef` is 9.0.7 against EF Core 10.0.10 -- it works, it just nags), and a `HostAbortedException` stack trace, which is how the EF tooling stops the host after building the service provider.

Notes on the build:

- A **Debug build of `WebApi` runs NSwag as a post-build step** (`NSwag` target in `WebApi/WebApi.csproj`): it generates `WebApi/wwwroot/api/specification.json` and the Angular client `WebApi/ApiClient/web-api-client.ts`. It boots the app's OpenAPI document provider, so a DI/startup regression fails the *build*, not just runtime. It does not need a database. Pass `-p:SkipNSwag=True` to skip it (faster, and required if the NSwag tool isn't restorable).
- In Development, startup applies migrations and seeds (`Program.cs`), so `dotnet run` needs SQL Server at `localhost\SQLEXPRESS`. To run without one, set `"UseInMemoryDatabase": true` in `WebApi/appsettings.json` (switch honored in `Infrastructure/Common/Extensions/ConfigureServices.cs`).
- Seeded dev credentials: `administrator@localhost` / `Administrator1!` (role `Administrator`).

### Secrets

`WebApi/appsettings.json` ships `JWT:Secret` and `Otp:Secret` as **empty strings** -- both are
validated with `ValidateOnStart()` (see `JwtOptions`, `OtpOptions`), so a real deployment that
does not supply them fails loudly at boot naming the missing setting, rather than throwing on the
first login or the first issued code. `WebApi/appsettings.Development.json` carries placeholder
values for both (beside the `Otp:StaticCode` escape hatch), so clone-and-run still works without
any extra setup.

To run against your own values instead of the committed placeholders, use user secrets (the
project already has a `UserSecretsId` in `WebApi/WebApi.csproj`):

```bash
dotnet user-secrets set "JWT:Secret" "<a real secret, at least 32 characters>" --project WebApi
dotnet user-secrets set "Otp:Secret" "<a real secret>" --project WebApi
```

Production should source both from a real secret store rather than configuration files or user
secrets. `Infrastructure` already references `Azure.Identity` (currently unused elsewhere) if Key
Vault is the destination.

## Architecture

Project references flow inward: `WebApi -> Infrastructure -> Application -> Domain`. Note `Infrastructure` references `Application` (not the reverse) -- abstractions such as `IApplicationDbContext`, `ICurrentUserService`, `IDateTime`, `IIdentityService` live in `Application/Common/Interfaces`, while repository/unit-of-work abstractions live in `Domain/Repositories`. Each layer exposes one `ConfigureServices` extension (`AddApplicationServices`, `AddInfrastructureServices`, `AddWebUIServices`) composed in `WebApi/Program.cs`.

### Time

`IDateTime` exposes a single member, `UtcNow`, and `DateTimeService` (registered as a singleton) is the only place in the solution permitted to read the system clock -- every persisted timestamp (`LoanApplications`, `OtpVerifications`, `Logs`, JWT expiry) is on the same UTC clock. A `Directory.Build.props` at the repository root adds `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `BannedSymbols.txt` banning `DateTime.Now`/`UtcNow`/`Today` and `DateTimeOffset.Now`/`UtcNow` (rule `RS0030`, `WarningsAsErrors`), so a raw clock read anywhere else is a build error, not a review comment. Test projects (`Domain.UnitTests`, `Application.UnitTests`) opt out via `NoWarn` because they legitimately construct fixed instants. Before this, `DateTimeService` returned *local* time while the Serilog sink and the log retention purge (`LogRetentionService`) read `DateTime.UtcNow` directly -- two tables in one database were timestamped on clocks that differed by the UTC offset, and local time is not monotonic across a DST fall-back.

### Request flow

Controller (thin, `ApiControllerBase.Mediator.Send`) -> `ValidationBehavior` -> `PerformanceBehavior` -> `OtpVerificationBehavior` (when command implements `IRequireOtpVerification`) -> handler -> aggregate repository -> `IUnitOfWork.SaveChangesAsync` -> `ApplicationDbContext.SaveChangesAsync` -> domain events dispatched -> response; exceptions shaped by `ApiExceptionFilterAttribute`.

**Queries do not take that path.** A query handler injects `IApplicationDbContext` and projects with `ProjectTo` straight to its DTO: no repository, no change tracker, no entity materialised only to be mapped and discarded. The read side has no invariants to protect, so it does not go through the write model.

### Conventions that matter when adding code

- **Vertical slices in `Application`**: `Application/<Feature>/{Commands,Queries,Dtos,Validators,EventHandlers}`. The MediatR request *and* its handler live in the same file (e.g. `CreateApplicationCommand.cs` holds `CreateApplicationCommand` + `CreateApplicationCommandHandler`). Commands/queries are `record`s implementing `IRequest`/`IRequest<T>`.
- **Registration is assembly-scan based** -- validators, MediatR handlers, AutoMapper profiles, and EF `IEntityTypeConfiguration`s are all discovered automatically. New files in the right place need no DI wiring.
- **DTOs map via `IMapFrom<T>`** (`Application/Common/Mappings`): implement the interface for the default map, or override `Mapping(Profile)` for custom members. `MappingProfile` reflects over the Application assembly.
- **Business rules live in the domain entity**, not the handler. Entities have `private set` properties, a `static Create(...)` factory and mutator methods that throw `DomainValidationException` and call `AddDomainEvent(...)` (see `Domain/Entities/LoanApplication.cs`). Handlers only orchestrate: resolve current user/time, call the entity, save.
- **FluentValidation validators handle input/lookup validation** (e.g. "does this CurrencyId exist"); they are injected with `IApplicationDbContext` and `IStringLocalizer`. An existence check is a read, so it queries the context rather than going through a repository.
- **Validator rules must be synchronous, and that is load-bearing.** Validators run *twice*: `ValidationBehavior` invokes them via `ValidateAsync`, and `AddFluentValidationAutoValidation()` (`WebApi/Extensions/ConfigureServices.cs`) also puts them in MVC's model-validation pipeline, which is **synchronous**. A single `MustAsync`/`CustomAsync` rule makes MVC throw `AsyncValidatorInvokedSynchronouslyException`, so the endpoint returns 500 before the handler is ever reached — while every unit test still passes, because none of them go through MVC and `ValidateAsync` is happy either way. Use `Must`/`Custom` with synchronous `Any`/`FirstOrDefault`; this is also why validator rules take no `CancellationToken`. `Infrastructure.UnitTests/Validators/ValidatorsAreSynchronousTests.cs` calls the synchronous `Validate` — exactly what MVC does — to keep this from regressing.
- **Domain events**: entities queue events; `ApplicationDbContext.SaveChangesAsync` calls `MediatorExtensions.DispatchDomainEvents` *before* persisting, publishing them to `INotificationHandler`s in `Application/<Feature>/EventHandlers`.
- **`IRepository<TEntity> where TEntity : BaseEntity`** is a deliberately **general-purpose** surface, so an unforeseen feature does not have to widen it. The constraint is `BaseEntity` rather than `IAggregateRoot` because this is reusable boilerplate: an entity that is not an aggregate root still needs somewhere to live. `IAggregateRoot` survives as documentation of which entities own a consistency boundary (`LoanApplication`, `OtpVerification`), not as a compile-time gate.
  - *Composable*: `Query()` / `QueryAsNoTracking()` return `IQueryable<TAggregate>` for anything the named members do not cover (grouping, joins, a projection).
  - *Reads*: `GetByIdAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `ListAsync`, `PageAsync`, `AnyAsync`, `CountAsync` — each taking an optional filter, ordering and include shaper.
  - *Writes*: `AddAsync`, `AddRangeAsync`, `Update`, `UpdateRange`, `Remove`, `RemoveRange`.

  Three properties of that surface are load-bearing, and each replaces something that used to be a bug:
  - **Includes are a typed shaper, not a string.** `include: q => q.Include(a => a.Currency).ThenInclude(…)` instead of `includeProperties: "Currency,LoanType"`. A typo in the old string returned a row with an unloaded navigation — no exception, just an empty name in the DTO — and no rename or find-usages ever followed it. The delegate is compiler-checked and supports chains.
  - **`Update` is a no-op for a tracked aggregate**, by design. `DbSet.Update` marks every property modified, rewriting columns that never changed and churning the `RowVersion` concurrency token; the change tracker already holds the delta. `Repository.Update` therefore calls it only when the entry is `Detached` — the disconnected case where there genuinely is no tracked original. So `entity.Mutate(); await uow.SaveChangesAsync();` is the normal write path and needs no `Update`, while a `QueryAsNoTracking()` result does.
  - **Reads default to tracked** (`asNoTracking: false`). A silently-dropped mutation is a bug; a redundant tracking entry is a perf nit. Read-only paths opt in with `asNoTracking: true`.

  `IRepository` names only BCL types (`IQueryable`, `Expression`, `Task`) — EF appears solely in the Infrastructure implementation, so `Domain` keeps no persistence dependency. `PageAsync` returns `(Items, TotalCount)` off one composed query so the two cannot disagree, and rejects a non-positive page with `DomainValidationException("InvalidPageNumber"/"InvalidPageSize")` — the same keys `GetApplicationsQueryValidator` uses, so the message is identical whether the edge or the repository caught it.
- **`IAggregateRoot`** (`Domain/Common/`) marks the entry point to an aggregate. Roots: `LoanApplication`, `OtpVerification`. Not roots: `Currency` and `LoanType`, which are reference data with no behaviour and no invariants and therefore **have no repository at all**; and `Log`, which is not a domain concept (the Serilog sink writes it, EF owns only the schema).
- **`IUnitOfWork` is `SaveChangesAsync` and nothing else** — the transactional boundary. It used to carry a property per repository, which made it a service-locator: a handler injected the registry and reached through it, so its real dependencies were invisible from its constructor and every test had to stub repositories the handler never touched. Handlers now inject the repositories they use. It is also not `IDisposable` — the `DbContext` is container-owned.
- **Adding an aggregate**: mark the root `IAggregateRoot`, interface in `Domain/Repositories`, implementation in `Infrastructure/Persistence/Repositories`, an `AddScoped` line in `AddInfrastructureServices`, and inject it into the handler. Nothing to add to `IUnitOfWork`. Repositories are the one thing that is *not* assembly-scanned, so a missing registration fails to resolve at startup.
- **Read-side projection**: query handlers use `.AsNoTracking().ProjectTo<TDto>(_mapper.ConfigurationProvider)`. `ProjectTo` builds a member-init expression tree, so **projected DTOs need public setters** — `IMapper.Map` tolerated `private set` only because it assigns by reflection. A `MapFrom` containing a method call will not translate; map it explicitly rather than letting it fall back to materialising entities. `GetApplicationsQuery` derives its count and its page from one `IQueryable`, so the two cannot drift apart the way two repository methods could.
- **Controllers**: inherit `ApiControllerBase`, route `api/v1/[controller]` with `[Route(nameof(Action))]` per action, `[Authorize]` at class level for protected controllers. One line per action delegating to `Mediator`.

### Cross-cutting behavior

- **Errors -> HTTP**: `WebApi/Filters/ApiExceptionFilterAttribute.cs` maps `ValidationException` (400 + `ValidationProblemDetails`), `NotFoundException` (404), `Unauthorized`/`ForbiddenAccessException`, and `DomainValidationExceptionWrapper` (400, message localized). `ValidationBehavior` is what wraps a raw `DomainValidationException` into `DomainValidationExceptionWrapper`. Anything unmapped is caught by `UnhandledExceptionHandlerMiddlware` and returned as a generic 500. **Domain exception messages are localization keys, not user text** -- add the key to `WebApi/Resources/localization.json` (`ka-GE` / `en-US`).
- **Localization**: the `x-sys-language` request header sets the culture (`SysLanguageMiddleware`); `JsonStringLocalizer` (registered as a singleton `IStringLocalizer`) reads `Resources/localization.json` from disk at construction. The header is stripped from the generated TS client via `excludedParameterNames` in `nswag.json`.
- **Auth**: ASP.NET Identity (`ApplicationUser : IdentityUser` with FirstName/LastName/PersonalNumber/BirthDate) + JWT bearer. `IdentityService` both implements identity operations and mints tokens from `JWT:Secret` / `JWT:ExpireMinutes`. Issuer/audience validation is disabled by design in the template.
- **MediatR behaviors are constrained `where TRequest : notnull`, not `where TRequest : IRequest<TResponse>`.** This is load-bearing. MediatR.Contracts 2.x made `IRequest` and `IRequest<T>` *unrelated* interfaces, so a void command (`: IRequest`) is not an `IRequest<Unit>`. MediatR still resolves `IPipelineBehavior<TCommand, Unit>` for it, the tighter constraint cannot be satisfied, and **the DI container silently skips the registration instead of failing** -- which left every void command (`UpdateApplicationCommand`, `UpdateApplicationStatusCommand`, `DeleteApplicationCommand`) with no validation, no performance logging and no OTP gate, surfacing domain exceptions as raw 500s. `OtpVerificationBehaviorTests.Handle_ForAVoidCommand_StillGate` fails to compile if the constraint is ever tightened back.
- **Two step verification (OTP)**: a command opts in by implementing `IRequireOtpVerification` (`Application/Common/Otp/`) and declaring `ChallengeId` + `[SensitiveData] OtpCode`. `OtpVerificationBehavior` does the rest: a request without a code gets a challenge issued and texted, then a **428 Precondition Required** carrying `challengeId`/`expiresAt`; the caller re-sends the same payload with the code. **Adding OTP to a new operation is implementing one interface -- there is no per-feature plumbing.** Samples: `RegisterUserCommand` (no user exists yet, so it overrides `OtpRecipient => PhoneNumber`) and `UpdateApplicationStatusCommand` (leaves it null, so the number is read off the authenticated account -- which is what stops a caller redirecting their own code to a phone they control).
  - `OtpPurpose` defaults to the command type name, so a code issued for one operation cannot be spent on another, and no registry needs maintaining.
  - **`OtpService.VerifyAsync` saves in a `finally`.** `OtpVerification.Verify` increments `AttemptCount` and *then* throws on a wrong code; saving only on the happy path would roll that increment back every time, `MaxAttempts` would never be reached, and six digits would be brute-forceable at leisure. The challenge is loaded tracked, so the increment reaches the database without any `Update` call.
  - `OtpService` injects `IOtpVerificationRepository` and `IUnitOfWork` directly. Its three lookups (`GetByChallengeIdAsync`, `CountRecentAsync`, `GetLatestAsync`) stay on the repository rather than moving to a query handler because each one feeds a decision that mutates the aggregate — they are write-side reads.
  - The challenge stores a `RequestHash` -- the payload with `ChallengeId`/`OtpCode` removed. Without it a caller could confirm a harmless request and then re-send a different one with the same code.
  - Codes are stored as a **keyed** HMAC (`Otp:Secret`), never plaintext: an unkeyed digest of six digits is a million candidates and reverses instantly from a leaked table.
  - `ISmsSender` has one implementation, `LoggingSmsSender`, which **logs the code instead of sending it** so the flow is runnable with no vendor account. It warns on every send. Replace it with a real provider (one class, one line in `AddInfrastructureServices`) before production.
  - OTP-carrying property names are in `RequestLogging:SensitiveProperties` **and** `LogRedactor.DefaultSensitiveProperties`. Both are needed: `BuildKeySet` uses the configured list *instead of* the defaults when config supplies one, so adding to only one leaves live codes in the `Logs` table.
- **Logging**: Serilog, composed in code by `AddApplicationLogging` (`WebApi/Extensions/LoggingConfiguration.cs`). Only *levels* live in config (the `Serilog` section of `appsettings.json`); sinks and the column mapping are C#, so a typo is a compile error rather than a silently dead column. `Program.cs` creates a `CreateBootstrapLogger()` before the container exists and ends with `Log.CloseAndFlush()` in a `finally` -- the database sink batches, so skipping the flush loses the tail.
  - **There is no `Logging` section in `appsettings.json`, deliberately.** `Microsoft.Extensions.Logging` filters run *before* Serilog sees an event, so keeping one would silently pre-filter events that `Serilog:MinimumLevel:Override` was going to allow. Level config has exactly one home.
  - **`LoggingMiddleware` writes one structured row per request** -- correlation id, method, url, status, duration, user, client ip, and the redacted request/response bodies. It is registered **outermost** (before `UseApplicationExceptionHandler`) so it observes the final status code and body of everything below it, including 500s. Its `try/finally` guarantees a row even when the request throws.
  - **Log columns come from two sources.** Message-template holes and `ILogger.BeginScope` values are both just structured properties to Serilog; `BuildColumnOptions` binds them to columns **by matching name**, so adding a property named like a column is all it takes. Request context that no single call site knows (correlation id, url, method, ip, username) is attached by `HttpContextEnricher` -- the replacement for NLog's ambient `${aspnet-*}` renderers, and the reason a warning raised deep in a handler still carries its request. `DefaultChannelEnricher` supplies `Channel=Api` via `AddPropertyIfAbsent`, which is why the middleware's explicit `Request` value wins. Property names are declared in `Application/Common/Logging/LogProperties.cs` and must stay in sync with `Domain/Entities/Log.cs` and `BuildColumnOptions`.
  - **`AutoCreateSqlTable` is off** -- EF owns the `Logs` schema. The consequence: the column set in `BuildColumnOptions` must match the table *exactly*, standard columns the table lacks (`Properties`, `MessageTemplate`, `LogEvent`, `TraceId`, `SpanId`, and the IDENTITY `Id`) are removed from `Store`, and a mismatch fails the whole batch reporting only to `Serilog.Debugging.SelfLog`. This is the one place where a mistake is silent, which is why `EnableSelfLog` writes `logs/serilog-selflog.txt` in **every** environment (the replacement for NLog's `internalLogFile`) -- check it first when the table stops filling.
  - **Routing rules**: the `ShouldPersist` filter on the database sub-logger -- request rows reach the database at `Information`, every other logger needs `Warning`+ to get in. `Channel` distinguishes `Request` rows from `Api` rows.
  - **Levels are spelled out**: `Information`/`Warning`/`Verbose`, not NLog's `Info`/`Warn`/`Trace`. `Logs.Level` is `nvarchar(16)` for this reason, and the `SerilogLogColumnAdjustments` migration rewrites historical rows so the table carries one vocabulary.
  - **Bodies are never blindly buffered.** `RequestLoggingOptions` (the `RequestLogging` section of `appsettings.json`) holds a content-type **allowlist**, a byte cap, a sensitive-property list and ignored paths. `BoundedResponseBufferStream` defers the capture decision to the first write, when `Content-Type` is known, so file downloads and uploads are recorded as `[body omitted: <type>, <n> bytes]` rather than copied into memory. `LogRedactor` masks passwords/tokens/etc. in JSON and form bodies, and is also applied by `PerformanceBehavior` before it logs a slow request.
  - `PerformanceBehavior` warns on handlers slower than 500 ms, logging the **redacted** request.
  - `LogRetentionService` (Infrastructure, `LogRetention` config section) purges rows past the retention window (default 90 days) in batches. Not registered when `UseInMemoryDatabase` is true.

## Known state / gotchas

- `WebApi/Dockerfile` still targets .NET 7 base/SDK images while the projects target net10.0 -- update the tags before relying on `docker-compose`.
- Deliberate misspellings/inconsistencies are load-bearing for existing `using`s: the folder `WebApi/Middlwares`, and the namespaces `WebUI.Filters` / `WebUI.Services` inside the WebApi project. (`CreateApplicationCommandhandler` was **not** one of them — nothing referenced it by name outside its own test, since handlers are resolved by interface, so it is now spelled `CreateApplicationCommandHandler`.)
- `WebApi/Filters/SwaggerAttributes.cs` is excluded from compilation via `<Compile Remove>`.
- `WebApi/ClientApp` contains only build output and empty folders (no `package.json`, nothing tracked under `src`); the Angular app is not part of this template -- only the generated `WebApi/ApiClient/web-api-client.ts` is.
- `dotnet build` emits NU1903 warnings for AutoMapper (pinned to 14.x/15.x to avoid the commercial license gate) and duplicate/prunable `PackageReference` warnings in `Infrastructure`/`WebApi`.
