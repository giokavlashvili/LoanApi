# Phase 5 — Service layering and aggregate ownership

**Depends on:** phase 4. **EF migration:** no. **Size:** large. **Optional / structural.**

> Like phase 4, this is a considered architectural change rather than a defect fix. Do not start
> it without phase 4 committed — it changes entity factory signatures, and doing it before
> phase 4 means touching every handler twice.

## Why

The service layer is placed by habit rather than by dependency direction, and the domain does
not own everything it should:

- `OtpService` sits in `Infrastructure` but contains no infrastructure — it orchestrates
  repositories, options, a hasher and a sender, all through interfaces. Its test already lives
  in `Application.UnitTests`, which means the Application test project references
  `Infrastructure` to test an Application concern.
- `IdentityService` does four jobs: user lifecycle, authorization, JWT minting, and publishing
  `UserCreatedEvent` through `IMediator` — a **second, parallel** event path alongside
  `BaseEntity.DomainEvents`.
- Audit fields are threaded through every factory signature (`Create(..., string createdById,
  DateTime created)`), so every call site must remember to pass them and every one is guarded by
  `#pragma warning disable CS8604`.
- `DeleteApplicationCommandHandler` raises `ApplicationDeletedEvent` from the **Application**
  layer, while create/update/status raise theirs from inside the aggregate.
- `LoanApplication` carries `[ForeignKey]` and `[NotMapped]` EF annotations, while every other
  entity is configured by Fluent API in `Infrastructure`.
- `IUserService` lives in `Domain/Common/Interfaces` — the domain declaring an outbound identity
  contract.

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green; phases 1–4 committed.

---

## Task 1 — Move `OtpService` to `Application`

Move `Infrastructure/Services/OtpService.cs` → `Application/Otp/Services/OtpService.cs`
(namespace `Application.Otp.Services`). Its dependencies — `IUnitOfWork`,
`IOtpVerificationRepository`, `IOtpCodeHasher`, `ISmsSender`, `IDateTime`,
`IOptionsMonitor<OtpOptions>`, `ILogger<T>` — are all abstractions already visible from
`Application`.

`HmacOtpCodeHasher` (crypto) and `LoggingSmsSender` (a vendor adapter) **stay in
Infrastructure**. That split is the point: policy in Application, mechanism in Infrastructure.

Registration moves too. `services.AddScoped<IOtpService, OtpService>()` goes from
`AddInfrastructureServices` to `AddApplicationServices`; `IOtpCodeHasher` and `ISmsSender`
registrations stay where they are.

**Fix the stale cref while you are here.** `Application/Common/Models/OtpOptions.cs` documents
`StaticCode` with `<see cref="Infrastructure.Services.OtpService"/>` — a reference from
`Application` into an assembly it does not reference, so it cannot resolve today. After the move
the type is reachable; update it to `<see cref="Application.Otp.Services.OtpService"/>` (or the
final namespace you land on). Sweep the rest of `Application` for any other
`cref="Infrastructure…"` while you are at it.

**The move is mechanical — there is no abstraction to invent.** In particular, the
`catch (DbUpdateException)` that phase 2 task 5 added to `IssueAsync` moves across unchanged:
`Application.csproj` already references `Microsoft.EntityFrameworkCore` 10.0.10, because
`IApplicationDbContext` exposes `DbSet<T>`. Do **not** add a `TrySaveChangesAsync` or any other
wrapper to hide EF from `Application` — the dependency already exists by design, and hiding one
exception type behind a new interface member buys nothing.

(If you want `Application` genuinely free of EF Core, that is a real and much larger discussion
about `IApplicationDbContext` exposing `DbSet<T>` and `EntityEntry<T>` at all. It is **out of
scope** for this phase. Do not start it.)

Move `Application.UnitTests/Otp/OtpServiceTests.cs` if its namespace needs it; the assertions do
not change. `Application.UnitTests` references `Infrastructure` today, and still needs to after
this task — `DateTimeServiceTests` (phase 1) lives there. Keep the reference.

## Task 2 — Split `IdentityService`

**Extract token minting.** New `Application/Common/Interfaces/IJwtTokenGenerator.cs`:

```csharp
public interface IJwtTokenGenerator
{
    (string Token, DateTime ValidTo) Generate(string userId, string userName, IEnumerable<string> roles);
}
```

Implementation `Infrastructure/Identity/JwtTokenGenerator.cs`, holding the `JwtSecurityToken`
construction, `SymmetricSecurityKey`, claims assembly and `IOptionsMonitor<JwtOptions>` (from
phase 3). `IdentityService.AuthenticateAsync` becomes: find user → check password → get roles →
delegate. Register as a singleton.

**Fix the status code.** `AuthenticateAsync` currently throws `NotFoundException("User not
found")` for a wrong password, which maps to **404** where **401** belongs. Introduce an
`InvalidCredentialsException` in `Application/Common/Exceptions/`, alongside the existing
`NotFoundException` and `ForbiddenAccessException`.

Map it in `WebApi/Filters/ApiExceptionFilterAttribute.cs` — **not** in the middleware. The
typed mapping is the `Dictionary<Type, Action<ExceptionContext>>` at ~line 23 of that filter
(registered via `options.Filters.Add<ApiExceptionFilterAttribute>()` in
`WebApi/Extensions/ConfigureServices.cs`); `WebApi/Middlwares/UnhandledExceptionHandlerMiddlware.cs`
is a blanket `catch (Exception) → 500` last resort and does no type dispatch. Add the dictionary
entry plus a handler method following the shape of the existing `HandleNotFoundException`.

Add the localization key to `WebApi/Resources/localization.json` in the existing
`{ "Key", "LocalizedValue": { "ka-GE", "en-US" } }` format — **both** locales.

Keep the message identical for "no such user" and "wrong password" — the current code does not
leak which, and it must stay that way.

**Decide the event path.** `IdentityService` publishes `UserCreatedEvent` directly via
`IMediator`. `ApplicationUser` is an Identity entity, not a domain aggregate, so it has no
`DomainEvents` collection to hang it on. Two acceptable outcomes — pick one and **document it in
`docs/architecture.md`**:

- *(preferred)* Keep the direct publish, rename the type to make the difference explicit, and
  add a comment stating this is an integration-style notification raised outside the aggregate
  event mechanism because Identity owns the user.
- Or introduce a domain `User` aggregate that mirrors the Identity user and owns the event.
  Substantially more work and more moving parts; only take this if the product needs a domain
  user anyway.

Do not leave it undocumented — an unexplained second event path is what makes the next reader
distrust both.

**Tidy while in the file** (small, safe): `UserExistsAsync`'s
`return user == null ? false : true;` → `return user is not null;`.

## Task 3 — Audit fields via a `SaveChanges` interceptor

Every factory and mutator takes `createdById` / `lastModifiedBy` and a timestamp, so every call
site must remember to pass `_currentUserService.UserId` and `_dateTime.UtcNow`, and the
nullability of `UserId` is papered over with `#pragma warning disable CS8604` in four files.

Create `Infrastructure/Persistence/Interceptors/AuditableEntityInterceptor.cs`:

```csharp
public sealed class AuditableEntityInterceptor(ICurrentUserService currentUser, IDateTime dateTime)
    : SaveChangesInterceptor
{
    // Stamp on both paths: SavingChanges and SavingChangesAsync.
    // Added   → Created, CreatedBy
    // Modified→ LastModified, LastModifiedBy
    // Also treat an entry with modified owned/related entries as Modified.
}
```

Registering it changes the shape of the `AddDbContext` call. Today both branches in
`AddInfrastructureServices` use the single-argument overload (`options => …`), which has no
access to the container. The interceptor depends on the **scoped** `ICurrentUserService`, so
switch to the two-argument overload — `provider` there is the scoped provider:

```csharp
services.AddScoped<AuditableEntityInterceptor>();

services.AddDbContext<ApplicationDbContext>((provider, options) =>
    options.UseSqlServer(...)
           .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
```

Apply it to the **in-memory branch too**, or auditing silently stops working whenever
`UseInMemoryDatabase` is true — including in the tests added below.

Then simplify the domain signatures:

- `LoanApplication.Create(int loanTypeId, decimal amount, int currencyId, int periodPerMonth)`
- `LoanApplication.Update(int loanTypeId, decimal amount, int currencyId, int periodPerMonth)`
- `LoanApplication.UpdateStatus(LoanStatus newStatus)`

**Keep the `InvalidUser` invariant.** It currently lives in the entity as
`if (string.IsNullOrWhiteSpace(lastModifiedBy)) throw new DomainValidationException("InvalidUser")`.
Once the entity no longer receives the user id it cannot check this — so move the check to the
command handlers (throw the same `DomainValidationException("InvalidUser")` when
`_currentUserService.UserId` is null or blank) or to a FluentValidation rule. **Do not silently
drop it**, and remove the corresponding `Domain.UnitTests` cases only after the equivalent
Application-level test exists.

`OtpVerification.Create` takes `created` and derives `CreatedBy` from `userId` — its OTP
semantics are distinct from generic auditing. **Leave `OtpVerification`'s signature alone**;
just let the interceptor fill `LastModified` where the entity does not.

Remove the now-unnecessary `#pragma warning disable CS8604` from the handler files.

## Task 4 — The aggregate owns its deletion event

`DeleteApplicationCommandHandler` does:

```csharp
_unitOfWork.LoanApplicationRepository.Remove(entity);
entity.AddDomainEvent(new ApplicationDeletedEvent(entity));
```

Add `LoanApplication.Delete()` which raises `ApplicationDeletedEvent(this)`, and have the
handler call `entity.Delete()` then `repository.Remove(entity)`. The event is then raised where
every other one is.

It works today only because dispatch happens before `SaveChanges` and the entity is still in the
change tracker in the `Deleted` state — a detail no handler should have to know.

## Task 5 — EF annotations out of the domain

`Domain/Entities/LoanApplication.cs` carries `[ForeignKey(nameof(LoanTypeId))]`,
`[ForeignKey(nameof(CurrencyId))]` and `[NotMapped]`. Move the equivalents into
`Infrastructure/Persistence/Configurations/LoanApplicationConfiguration.cs`:

```csharp
builder.HasOne(l => l.LoanType).WithMany().HasForeignKey(l => l.LoanTypeId);
builder.HasOne(l => l.Currency).WithMany().HasForeignKey(l => l.CurrencyId);
builder.Ignore(l => l.CreatedByUser);
```

`BaseEntity.DomainEvents` keeps its `[NotMapped]` — it is on the base type and mapping it
through configuration for every entity is worse than the one annotation.

**Generate a migration afterwards and confirm it is empty.** Delete relationships configured
by annotation and re-declared by Fluent API should produce no schema change. A non-empty
migration means the Fluent configuration does not match what the annotations produced —
most likely the delete behaviour (`OnDelete`). Fix the configuration to match rather than
accepting a schema change in a refactoring phase.

While in the entity, reconsider `[NotMapped] public User? CreatedByUser { get; set; }` — the one
public settable property on an otherwise encapsulated aggregate. If nothing populates it (check
the query handlers' projections and the controllers — `MappingProfile` was deleted with
AutoMapper on 2026-08-08), delete it. If something does, make the setter private and
add a method that sets it.

## Task 6 — `IUserService` moves to `Application`

Move `Domain/Common/Interfaces/IUserService.cs` → `Application/Common/Interfaces/IUserService.cs`
and `Domain/Common/Models/User.cs` → `Application/Common/Models/User.cs` if nothing in `Domain`
still needs it (check — `LoanApplication.CreatedByUser` references `User`, so this task depends
on the `CreatedByUser` decision in task 5).

`IIdentityService : IUserService` then sits entirely in `Application`, and `Domain` stops
declaring outbound service contracts. If `Domain` still needs `User` after task 5, **skip this
task** and note why in the report — do not create a duplicate type.

## Task 7 — Documentation

- `.cursor/rules/00-project-core.mdc` — where services live; the Application/Infrastructure
  split criterion (policy vs mechanism)
- `.cursor/rules/domain-entities.mdc` — factories no longer take audit fields; the interceptor
  stamps them; entities own their deletion events; no EF annotations in `Domain`
- `.cursor/rules/auth-identity.mdc`, `auth-identity-infra.mdc` — the `IdentityService` /
  `IJwtTokenGenerator` split, the 401 change
- `.cursor/rules/otp-infrastructure.mdc`, `otp.mdc` — `OtpService` now lives in `Application`
- `.cursor/skills/add-vertical-slice/*` — factory signatures in the sample code
- `docs/architecture.md` — services section, the audit interceptor, the documented decision on
  the `UserCreatedEvent` path

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Plus, because the interceptor is easy to get subtly wrong, add `Infrastructure.UnitTests` cases
on the in-memory provider asserting that:

- a newly added `LoanApplication` gets `Created` and `CreatedBy` populated from
  `ICurrentUserService` / `IDateTime` without the handler passing them;
- a modified one gets `LastModified` / `LastModifiedBy` and does **not** have `Created`
  overwritten.

If a database is available, run the API and exercise login (401 on bad password), registration,
and the loan endpoints.

## Definition of done

- [ ] `OtpService` in `Application/Otp/Services`, registered in `AddApplicationServices`, with
      its `DbUpdateException` catch carried over unchanged and **no** new wrapper on `IUnitOfWork`
- [ ] `OtpOptions`'s `<see cref="Infrastructure…"/>` updated to the new namespace
- [ ] `IJwtTokenGenerator` extracted; `IdentityService` no longer mints tokens
- [ ] Bad credentials return 401, with the same message for unknown user and wrong password
- [ ] The `UserCreatedEvent` path is decided and documented
- [ ] `AuditableEntityInterceptor` registered on both provider branches; `LoanApplication`'s
      factory/mutators no longer take audit parameters; the `InvalidUser` invariant is enforced
      somewhere and tested
- [ ] `LoanApplication.Delete()` raises the deletion event
- [ ] No EF annotations left in `Domain/Entities`; the confirming migration is empty
- [ ] `IUserService` moved, or the report explains why it stayed
- [ ] All documentation files updated
- [ ] Build green, tests green, interceptor tests added

## Out of scope

- Do not add a transactional outbox or move domain-event dispatch after commit. It is a real
  gap — events are published before the save, so a handler observes uncommitted state and a
  failed save leaves them already published — but it needs its own design pass and its own
  phase. **Record it in `docs/architecture.md` as a known limitation** and stop there.
- Do not add integration tests against real SQL Server, retry policies, or health checks.
- Do not rename `ApplicationDbContext`, restructure folders, or touch the logging pipeline.

## Commit

```
Place services by dependency direction and give the aggregate its audit and events

OtpService held no infrastructure yet lived in Infrastructure; it moves to
Application, with the hasher and SMS adapter staying behind as the mechanisms
it depends on. IdentityService did user lifecycle, authorization, JWT minting
and event publishing at once; token generation is now its own service and bad
credentials return 401 rather than 404.

Audit fields are stamped by a SaveChanges interceptor instead of being threaded
through every factory signature, LoanApplication raises its own deletion event,
and EF annotations move out of the domain into the entity configuration.
```
