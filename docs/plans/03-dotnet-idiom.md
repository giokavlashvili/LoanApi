# Phase 3 — .NET idiom and hygiene

**Depends on:** phase 2. **EF migration:** no. **Size:** medium.

Six independent tasks. None changes architecture; all bring the code onto patterns the repo
already uses elsewhere, or removes a foot-gun. Order does not matter — but land them as one
commit.

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green; phases 1 and 2 committed.

---

## Task 1 — JWT configuration onto the options pattern

`Infrastructure/Identity/IdentityService.cs` reads configuration by string indexing:

```csharp
var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JWT:Secret"]));
expires: _dateTime.UtcNow.AddMinutes(int.Parse(_config["JWT:ExpireMinutes"])),
```

behind a file-level `#pragma warning disable CS8604`. A missing `JWT:ExpireMinutes` is a
`FormatException` on the first login attempt rather than a startup failure, and a missing
`JWT:Secret` is a `NullReferenceException` inside the crypto call.

Every other configuration section in this repo is already strongly typed — `OtpOptions`,
`PaginationOptions`, `LogRetentionOptions`. JWT is the odd one out.

### Changes

1. Create `Application/Common/Models/JwtOptions.cs`, modelled on the existing `OtpOptions`
   (match its style: `SectionName` constant, plain properties, XML docs on anything non-obvious):

   ```csharp
   public class JwtOptions
   {
       public const string SectionName = "JWT";

       [Required, MinLength(32)]
       public string Secret { get; set; } = string.Empty;

       [Range(1, 1440)]
       public int ExpireMinutes { get; set; } = 180;
   }
   ```

   `MinLength(32)` is not arbitrary: HMAC-SHA256 with a key shorter than the hash output is
   accepted by `SymmetricSecurityKey` but weakens the signature, and the committed placeholder
   is already longer than that, so nothing breaks.

2. Register with validation in `Application/Extensions/ConfigureServices.cs`, next to the
   existing `services.Configure<OtpOptions>(...)`:

   ```csharp
   services.AddOptions<JwtOptions>()
       .Bind(configuration.GetSection(JwtOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();
   ```

   `ValidateOnStart()` turns a misconfiguration into a failure at boot with a clear message,
   which is the whole point.

   **Bring `OtpOptions` along.** It currently has no validation at all — plain properties with
   defaults, and `HmacOtpCodeHasher.Compute` throws `InvalidOperationException` on an empty
   `Secret` at first use. Leaving it that way means the two secrets in this codebase fail in two
   different ways at two different times. Convert its registration in
   `Application/Extensions/ConfigureServices.cs` from `services.Configure<OtpOptions>(...)` to
   the same `AddOptions().Bind().ValidateDataAnnotations().ValidateOnStart()` chain, and
   annotate at minimum `Secret` (`[Required]`), `CodeLength` (`[Range(4, 10)]`) and
   `MaxAttempts` (`[Range(1, 20)]`). Leave `HmacOtpCodeHasher`'s runtime guard in place — it is
   cheap and it covers a secret cleared at runtime through `IOptionsMonitor`.

3. Inject `IOptionsMonitor<JwtOptions>` into `IdentityService` (matching how `OtpService`
   consumes `IOptionsMonitor<OtpOptions>`), drop the `IConfiguration` dependency, and remove the
   now-unneeded `#pragma warning disable CS8604` if nothing else in the file needs it.

4. `Infrastructure/Common/Extensions/ConfigureServices.cs` reads `configuration["JWT:Secret"]`
   again when configuring `AddJwtBearer` (~line 98). Bind the section there too so the signing
   key and the validation key cannot drift apart:

   ```csharp
   var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
       ?? throw new InvalidOperationException("The JWT configuration section is missing.");
   ```

   Also review the `TokenValidationParameters` while you are there: `ValidateIssuer` and
   `ValidateAudience` are both `false`. That is defensible for a template with no issuer
   configured — **do not change the behaviour**, but add a comment saying it is deliberate and
   what a real deployment should set. Leave `RequireHttpsMetadata = false` alone for the same
   reason; note it in the report.

---

## Task 2 — Thread `CancellationToken` through the async paths

Several async methods accept no token, and several call sites drop the one they have. A
cancelled HTTP request still runs its queries to completion.

**Interfaces to change** (`Domain/Repositories/`):

| Member | Now | Target |
|---|---|---|
| `IRepository.GetAllAsync()` | none | `GetAllAsync(CancellationToken ct = default)` |
| `ILoanApplicationRepository.GetCountAsync()` | none | `GetCountAsync(CancellationToken ct = default)` |
| `ILoanApplicationRepository.GetPaginatedListAsync(int, int)` | none | `+ CancellationToken ct = default` |

Then pass the token into the underlying `ToListAsync`/`CountAsync` in
`Infrastructure/Persistence/Repositories/`.

**Call sites that must stop dropping the token:**

- `Application/LoanApplications/Commands/CreateApplicationCommand.cs` — `AddAsync(entity)` →
  `AddAsync(entity, cancellationToken)`
- `Application/LoanApplications/Commands/{Delete,Update,UpdateStatus}ApplicationCommand.cs` —
  `GetByIdAsync(request.Id)` → `GetByIdAsync(request.Id, cancellationToken)`
- `Application/LoanApplications/Queries/GetApplicationsQuery.cs` — both repository calls
- `Application/Currencies/Queries/GetCurrenciesQuery.cs`,
  `Application/LoanTypes/Queries/GetLoanTypesQuery.cs` — `GetAllAsync()`

Sweep for any remaining `Async(` call inside a method that has a `cancellationToken` in scope
but does not forward it.

---

## Task 3 — Register repositories in the container

`UnitOfWork`'s constructor takes the four repositories as **optional** parameters, and none of
them is registered — so DI always supplies `null` and the constructor falls back to
`new CurrencyRepository(_context)` and friends. The optional parameters exist only so unit tests
can pass mocks, which is the container's job.

In `Infrastructure/Common/Extensions/ConfigureServices.cs`, beside the existing
`services.AddScoped<IUnitOfWork, UnitOfWork>()`:

```csharp
services.AddScoped<ICurrencyRepository, CurrencyRepository>();
services.AddScoped<ILoanTypeRepository, LoanTypeRepository>();
services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
services.AddScoped<IOtpVerificationRepository, OtpVerificationRepository>();
```

Then make the `UnitOfWork` parameters **required** (drop the `? = null` defaults and the
`?? new ...` fallbacks). Tests construct `UnitOfWork` directly and pass mocks — they keep
working, and they get clearer.

`.cursor/rules/infrastructure-ef.mdc` and `.cursor/skills/add-vertical-slice/*` describe adding
a repository; update them to include the DI registration line, or the next generated slice will
be unregistered.

> Phase 4 removes the repository properties from `IUnitOfWork` entirely, and deletes
> `ICurrencyRepository` / `ILoanTypeRepository` outright (they are reference data, not aggregate
> roots). So two of the four registrations added here are short-lived if you go on to phase 4.
> Register all four anyway: phase 3 has to leave a working, consistent state on its own, and the
> two that survive are the prerequisite for injecting repositories directly into handlers.

---

## Task 4 — `IUnitOfWork` should not be `IDisposable`

`Domain/Repositories/IUnitOfWork.cs` declares `: IDisposable`, and `UnitOfWork.Dispose()`
disposes `_context` — a `DbContext` the container owns and will dispose itself at the end of
the scope. Application-layer code should not be able to dispose the shared context at all.

- Remove `: IDisposable` from `IUnitOfWork`.
- Remove `Dispose`, `Dispose(bool)`, the `disposed` field and the `GC.SuppressFinalize` call
  from `UnitOfWork`.
- If anything calls `Dispose()` or wraps the unit of work in a `using`, remove that too.

The `IApplicationDbContext : IDisposable` declaration is a separate question — **leave it**;
phase 4 addresses the read/write split that makes it moot.

---

## Task 5 — Constant-time comparison of the OTP hashes

`Domain/Entities/OtpVerification.Verify` compares HMAC outputs with
`string.Equals(..., StringComparison.Ordinal)`, which short-circuits on the first differing
character.

Low severity — HMAC-SHA256 cannot be inverted from a timing signal without the key — but the
fix is one line and removes the question permanently:

```csharp
if (!CryptographicOperations.FixedTimeEquals(
        Convert.FromBase64String(CodeHash!), Convert.FromBase64String(candidateHash)))
```

Guard the decode: a malformed Base64 candidate must fail as a wrong code, not as a
`FormatException` escaping to a 500. Wrap the comparison in a small private static helper on the
entity that returns `false` on `FormatException`, and apply it to the `RequestHash` comparison
too. `System.Security.Cryptography` in `Domain` is acceptable here — it is a BCL primitive, not
an infrastructure dependency.

Add a `Domain.UnitTests` case: a malformed Base64 candidate hash is rejected as an invalid code
and still increments `AttemptCount`.

---

## Task 6 — Get the secrets out of `appsettings.json`

`WebApi/appsettings.json` contains literal values for `JWT:Secret` and `Otp:Secret`, both
committed to git.

They are obviously placeholders (`"...ChangeMeBeforeProduction"`), and this is a template — so
the goal is not to pretend they are live secrets, it is to make the correct path the default:

1. **Do not simply empty both values.** With task 1's `ValidateOnStart`, an empty `JWT:Secret`
   in `appsettings.json` means the application will not boot after a fresh clone — for a
   boilerplate whose whole value is that it runs immediately, that is a regression, not
   hardening.

   Instead:
   - `WebApi/appsettings.json` — both secrets become empty strings. This is the file that
     represents a real deployment, and a real deployment must supply them.
   - `WebApi/appsettings.Development.json` — move the existing placeholder values here, beside
     the `Otp:StaticCode` that already lives there for exactly this reason. Clone-and-run still
     works; nothing environment-specific leaks into the base file.

   A production deployment then fails loudly at boot with a message naming the missing setting,
   which is the behaviour worth having.

   Note that `OtpOptions` (see task 1) has **no** validation attributes today —
   `HmacOtpCodeHasher.Compute` throws `InvalidOperationException` at first use instead. Task 1
   adds matching `ValidateOnStart` validation to it, so after this phase both secrets fail the
   same way, at boot.
2. Initialise user secrets for `WebApi` and document the two `dotnet user-secrets set` commands
   in `docs/architecture.md` under the run instructions, plus a line in the README/architecture
   getting-started notes.
3. Note in `docs/architecture.md` that production should source both from a secret store, and
   that `Azure.Identity` is already referenced by `Infrastructure` (currently unused) if Key
   Vault is the destination.

Do **not** add a Key Vault provider or any cloud dependency in this phase — just stop shipping
committed secrets and make the failure mode loud.

---

## Task 7 — Package version hygiene

Two problems found by auditing the `.csproj` files. Both matter more than they look, and the
first is a prerequisite for phase 4.

### AutoMapper is split across the solution

| Project | AutoMapper |
|---|---|
| `Domain`, `Application`, `Infrastructure`, `WebApi` | **14.0.0** |
| `Application.UnitTests`, `Domain.UnitTests` | **15.1.0** |

NuGet unifies to the highest version within each project's closure, so **the tests exercise a
different AutoMapper than production runs**. Phase 4 rewrites the query handlers onto
`ProjectTo`, whose expression-building behaviour is exactly the kind of thing that differs
across a major version — a passing test would prove nothing about the deployed app.

Unify on one version across all six projects. Prefer moving everything to **14.0.0** (match
production, smallest change, no behavioural risk in this phase). If you choose 15.1.0 instead,
you must read AutoMapper's 15.0 release notes for breaking changes, verify `MappingProfile`'s
reflection-based `ApplyMappingsFromAssembly` and the `IMapFrom<T>` default interface method
still work, and run the full suite — do not upgrade blind.

Better still, since this repo has no central package management: consider adding the versions to
the `Directory.Build.props` created in phase 1, or introducing `Directory.Packages.props` with
`ManagePackageVersionsCentrally`. That is optional; the required outcome is one AutoMapper
version, solution-wide.

While you are in the `.csproj` files, note (do not necessarily fix) that
`Microsoft.Data.SqlClient` and the deprecated `System.Data.SqlClient` are **both** referenced by
`Infrastructure`, `WebApi`, `Application` and the test projects, and no code uses either
directly — EF Core and the Serilog sink bring their own. Report it; removing the dead
`System.Data.SqlClient` reference is safe but is not required by this phase.

### `Domain` references AutoMapper

`Domain/Domain.csproj` has a `PackageReference` to AutoMapper. A domain project should not
depend on a mapping library — mapping is an Application concern, and every DTO and profile in
this solution already lives in `Application`.

Confirm nothing in `Domain` uses it:

```bash
grep -rn "AutoMapper" --include=*.cs Domain
```

If that returns nothing, remove the `PackageReference` and rebuild. If it returns something,
leave the reference, and report what uses it — that is a layering problem worth its own fix, not
something to force here.

---

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Then confirm startup validation works as intended: with `JWT:Secret` unset, the app must fail at
boot with a message naming the missing setting — not throw on the first login. Verify by running
`dotnet run --project WebApi` with no user secrets configured, and confirm the message.

## Definition of done

- [ ] `JwtOptions` exists, is bound with `ValidateDataAnnotations().ValidateOnStart()`, and is
      consumed by both `IdentityService` and the `AddJwtBearer` setup
- [ ] No `IConfiguration` string indexing left in `IdentityService`; the `CS8604` pragma removed
      if it is no longer needed
- [ ] `CancellationToken` accepted and forwarded on every async repository member and every
      handler call site
- [ ] Four repositories registered in DI; `UnitOfWork`'s parameters are required
- [ ] `IUnitOfWork` no longer `IDisposable`; `UnitOfWork.Dispose` gone
- [ ] `FixedTimeEquals` used for both hash comparisons, malformed Base64 handled, test added
- [ ] Secrets removed from `appsettings.json` and moved to `appsettings.Development.json`;
      clone-and-run still works; a production-style run fails loudly; user-secrets documented
- [ ] `OtpOptions` validated on start alongside `JwtOptions`
- [ ] One AutoMapper version across all six projects; `Domain`'s AutoMapper reference removed or
      its use reported
- [ ] `.cursor/rules/infrastructure-ef.mdc` and the `add-vertical-slice` skill mention the
      repository DI registration
- [ ] Build green, tests green

## Out of scope

- Do not remove the repository properties from `IUnitOfWork` — phase 4.
- Do not move `OtpService` or split `IdentityService` into a token service — phase 5.
- Do not change `ValidateIssuer` / `ValidateAudience` / `RequireHttpsMetadata` behaviour;
  comment them and report.

## Commit

```
Adopt the options pattern for JWT, thread cancellation, and register repositories

- JWT configuration was read by string indexing behind a nullability pragma;
  it is now JwtOptions, validated on start, and shared by IdentityService and
  the bearer setup so the two keys cannot drift.
- Async repository members accepted no CancellationToken and handlers dropped
  the one they had.
- The four repositories were never registered; UnitOfWork's optional
  constructor parameters always resolved to null and it new'd them itself.
- IUnitOfWork no longer exposes IDisposable over a container-owned DbContext.
- OTP hash comparison is now constant-time.
- Placeholder secrets no longer ship in appsettings.json; a missing JWT secret
  fails at boot instead of on first login.
```
