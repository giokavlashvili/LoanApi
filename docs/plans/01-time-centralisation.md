# Phase 1 — Centralise time on `IDateTime`, in UTC

**Depends on:** nothing. **EF migration:** no. **Size:** small.

## Why

The application currently reads the clock from two different places, on two different clocks:

| Writer | Source | Result |
|---|---|---|
| `DateTimeService.Now` | `DateTime.Now` — **local time** | `LoanApplications.Created`, `OtpVerifications.Created/ExpiresAt`, JWT expiry |
| `LogRetentionService` | `DateTime.UtcNow` directly — bypasses the abstraction | retention cutoff |
| Serilog MSSqlServer sink | `ConvertToUtc = true` | `Logs.When` |

So in UTC+4, `Logs.When` sits four hours behind `LoanApplications.Created` **in the same
database**. Correlating a request log with the row it produced is off by the UTC offset, and
any future "what happened around this time" query silently compares two clocks.

Local time is also not monotonic. At the DST fall-back the same wall-clock hour repeats, which
makes `OtpVerificationRepository.GetLatestAsync`'s `OrderByDescending(o => o.Created)` return
the wrong challenge, and makes the resend cooldown (`previous.Created.Add(cooldown) > now`)
behave incorrectly for that hour.

The fix is not to remove the abstraction — it is to make the abstraction the **only** source of
time, and to make that source UTC.

> Note for the executing agent: `TimeProvider` (.NET 8+) is the framework-standard alternative.
> It was considered and **deliberately rejected** for this repo: `IDateTime` is already an
> established, documented convention across the rules files and skills. Do not introduce
> `TimeProvider`.

## Preconditions — verify before starting

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both must pass. Confirm these exist and read as described:

- `Application/Common/Interfaces/IDateTime.cs` declares a single `DateTime Now { get; }`
- `Infrastructure/Services/DateTimeService.cs` returns `DateTime.Now`
- `Infrastructure/Common/Extensions/ConfigureServices.cs` registers it with `AddTransient`
- There is **no** `Directory.Build.props` at the repository root

If any differs, stop and report.

## Step 1 — Redefine the abstraction

`Application/Common/Interfaces/IDateTime.cs`:

```csharp
namespace Application.Common.Interfaces
{
    /// <summary>
    /// The single source of "now" for the whole application. Always UTC.
    /// <para>
    /// Nothing outside <c>DateTimeService</c> may read the system clock directly — a
    /// <c>BannedApiAnalyzers</c> rule makes <see cref="DateTime.Now"/> and friends a build
    /// error. The reason is not testability alone: the previous implementation returned
    /// <em>local</em> time while the Serilog sink and the log retention purge used UTC, so two
    /// tables in one database were timestamped on clocks that differed by the UTC offset.
    /// </para>
    /// </summary>
    public interface IDateTime
    {
        DateTime UtcNow { get; }
    }
}
```

`Infrastructure/Services/DateTimeService.cs`:

```csharp
using Application.Common.Interfaces;

namespace Infrastructure.Services
{
    /// <summary>
    /// The one place in the solution permitted to read the system clock.
    /// </summary>
    public sealed class DateTimeService : IDateTime
    {
        // The single sanctioned use of the system clock; see IDateTime for why everything
        // else has to go through this property.
#pragma warning disable RS0030 // Do not use banned APIs
        public DateTime UtcNow => DateTime.UtcNow;
#pragma warning restore RS0030
    }
}
```

**Naming is deliberate**: `Now` returning a UTC value is the same trap one level down. The
property name has to state the kind.

## Step 2 — Register as a singleton

`Infrastructure/Common/Extensions/ConfigureServices.cs`, currently line ~68:

```csharp
- services.AddTransient<IDateTime, DateTimeService>();
+ services.AddSingleton<IDateTime, DateTimeService>();
```

It is stateless, and `LogRetentionService` (a singleton `BackgroundService`) needs to inject it
in step 4.

## Step 3 — Update every production call site

Exactly five files. Each is a `_dateTime.Now` → `_dateTime.UtcNow` rename except where noted.

| File | Line (approx) | Change |
|---|---|---|
| `Application/LoanApplications/Commands/CreateApplicationCommand.cs` | 40 | `_dateTime.Now` → `_dateTime.UtcNow` |
| `Application/LoanApplications/Commands/UpdateApplicationCommand.cs` | 42 | same |
| `Application/LoanApplications/Commands/UpdateApplicationStatusCommand.cs` | 50 | same |
| `Infrastructure/Services/OtpService.cs` | 51, 126 | same |
| `Infrastructure/Identity/IdentityService.cs` | 188 | same — see note below |

`IdentityService.GetToken` passes the value as `JwtSecurityToken`'s `expires`. This one was
*not* producing a wrong token: `System.IdentityModel.Tokens.Jwt` normalises a non-UTC
`DateTime` before writing the `exp` claim. The change makes the intent explicit and keeps the
`validTo` returned to the caller on the same clock as everything else. Do not "fix" anything
else in this file — it is phase 3 and 5 work.

## Step 4 — Bring `LogRetentionService` onto the abstraction

`Infrastructure/Services/LogRetentionService.cs` currently computes its cutoff from
`DateTime.UtcNow` directly (line ~70). This is the decentralisation the phase exists to remove.

- Add `IDateTime` to the constructor, store it in a field.
- Replace `var cutoff = DateTime.UtcNow.AddDays(-options.RetentionDays);` with
  `var cutoff = _dateTime.UtcNow.AddDays(-options.RetentionDays);`

The value is unchanged (both are UTC) — the point is that there is now exactly one clock reader.

## Step 5 — Make it stay centralised: ban the raw clock

Without this, the next feature reintroduces `DateTime.Now` and nothing notices.

Create `Directory.Build.props` at the repository root.

**The condition is mandatory, not cosmetic.** `docker-compose.dcproj` is a member of
`LoanApi.sln` and uses `Microsoft.Docker.Sdk`, which does not understand `PackageReference` or
`AdditionalFiles`. An unconditional `Directory.Build.props` is imported by it too and breaks the
solution build.

```xml
<Project>
  <!-- C# projects only. docker-compose.dcproj (Microsoft.Docker.Sdk) is in the solution and
       cannot restore a PackageReference; without this guard it fails the solution build. -->
  <ItemGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>

  <PropertyGroup Condition="'$(MSBuildProjectExtension)' == '.csproj'">
    <!-- A raw clock read is a build error, not a suggestion. -->
    <WarningsAsErrors>$(WarningsAsErrors);RS0030</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

After creating it, build the **solution** (not just a project) to confirm the `.dcproj` still
loads:

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

Create `BannedSymbols.txt` at the repository root:

```
P:System.DateTime.Now;Use IDateTime.UtcNow — the application has one clock, and it is UTC.
P:System.DateTime.UtcNow;Use IDateTime.UtcNow so the clock stays injectable and testable.
P:System.DateTime.Today;Use IDateTime.UtcNow.
P:System.DateTimeOffset.Now;Use IDateTime.UtcNow.
P:System.DateTimeOffset.UtcNow;Use IDateTime.UtcNow.
```

Then exempt the two legitimate cases:

1. `DateTimeService` — already handled by the `#pragma` in step 1.
2. **Test projects.** Tests construct fixed instants and must stay free to do so. In both
   `Application.UnitTests/Application.UnitTests.csproj` and
   `Domain.UnitTests/Domain.UnitTests.csproj`, add inside a `<PropertyGroup>`:

   ```xml
   <NoWarn>$(NoWarn);RS0030</NoWarn>
   ```

If `Microsoft.CodeAnalysis.BannedApiAnalyzers` 4.14.0 does not restore, use the newest
version that does and note it in the report. If the package cannot be restored at all
(offline feed), **skip step 5 entirely**, complete the rest of the phase, and say so clearly in
the report — the phase is still worth landing without it.

## Step 6 — Update the tests

Production behaviour changes, so these must change with it.

- `Application.UnitTests/Otp/OtpServiceTests.cs` (~line 52):
  `_dateTime.Setup(d => d.Now).Returns(Now);` → `.Setup(d => d.UtcNow)`.
  Check the `Now` constant the test uses; if it is built with `DateTime.Now` or an unspecified
  `Kind`, replace it with an explicit
  `new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`.
- `Application.UnitTests/Common/Behaviours/OtpVerificationBehaviorTests.cs` (~line 70):
  `DateTime.Now.AddMinutes(5)` → a fixed UTC instant.
- `Application.UnitTests/LoanApplications/Commands/{Create,Update,Delete}LoanApplicationTests.cs`:
  `Mock<IDateTime>` setups and any `DateTime.Now` literals → fixed UTC instants.
- `Domain.UnitTests/Entities/LoanApplicationTests.cs`: replace every `DateTime.Now` argument
  with a fixed UTC instant. These are pure arrangement values; the assertions do not change.

**Add one new test** that would have caught the original defect —
`Application.UnitTests/Common/DateTimeServiceTests.cs`:

```csharp
[Test]
public void UtcNow_ReturnsUtcKind()
{
    Assert.That(new DateTimeService().UtcNow.Kind, Is.EqualTo(DateTimeKind.Utc));
}
```

(`Application.UnitTests` already references `Infrastructure` — `OtpServiceTests` constructs
`OtpService` directly — so no new project reference is needed. Verify this; if the reference is
absent, put the test in a location that can see `DateTimeService` rather than adding one.)

## Step 7 — Update the documentation

Search for `IDateTime` and `.Now` across `.cursor/` and `docs/`, and update:

- `.cursor/rules/00-project-core.mdc` — the abstractions list mentions `IDateTime`; add that it
  is UTC-only and that raw clock reads are banned.
- `.cursor/rules/application-cqrs.mdc` — the handler shape line reads
  "current user / `IDateTime` → entity factory"; update the member name to `UtcNow`.
- `.cursor/rules/testing.mdc` — mocking guidance; update the member name, and note that tests
  should use fixed UTC instants, not `DateTime.Now`.
- `.cursor/skills/add-vertical-slice/*` — if any sample code shows `_dateTime.Now`, update it.
  **This is the file that generates new code; missing it undoes the phase.**
- `docs/architecture.md` — the abstractions paragraph (~line 60). Add a short subsection stating
  that all persisted timestamps are UTC, that `IDateTime.UtcNow` is the only clock, and that
  `BannedSymbols.txt` enforces it.

## Existing data

Rows written before this change hold **local** times in `LoanApplications.Created`,
`LoanApplications.LastModified` and the `OtpVerifications` columns; `Logs.When` is already UTC.

Default decision: **accept the discontinuity and do not convert.** This is a boilerplate with
seeded sample data; the rows are disposable, and a conversion script that guesses the offset is
worse than a clean break. If the executing agent finds this repo has meaningful data, stop and
report rather than deciding.

Nothing in the schema changes — the columns are `datetime2` and stay `datetime2`. **No EF
migration is required in this phase.** If `dotnet ef migrations add` is run and produces a
non-empty migration, something has gone wrong; investigate rather than committing it.

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Then confirm the centralisation actually holds — this must return **no** hits outside
`DateTimeService.cs` and the two test projects:

```bash
grep -rn "DateTime\.Now\|DateTime\.UtcNow\|DateTimeOffset\.Now\|DateTimeOffset\.UtcNow" --include=*.cs Domain Application Infrastructure WebApi
```

## Definition of done

- [ ] `IDateTime` exposes `UtcNow` only; `Now` is gone from the interface and all call sites
- [ ] `DateTimeService` returns `DateTime.UtcNow` and is registered as a singleton
- [ ] `LogRetentionService` takes its cutoff from `IDateTime`, not the static clock
- [ ] `Directory.Build.props` + `BannedSymbols.txt` make a raw clock read a build error (or the
      report explains why the analyzer could not be added)
- [ ] Test projects opt out of `RS0030`; all tests use fixed UTC instants
- [ ] `DateTimeServiceTests.UtcNow_ReturnsUtcKind` exists and passes
- [ ] Rules, skills and `docs/architecture.md` describe `UtcNow` and the ban
- [ ] Build green, tests green, the `grep` above returns nothing

## Out of scope — do not do these here

- Do not introduce `TimeProvider`, `DateTimeOffset` columns, or change any column type.
- Do not touch `IdentityService` beyond the single `_dateTime.UtcNow` rename (its
  `IConfiguration` usage and JWT split are phases 3 and 5).
- Do not fix `Repository.GetByIdAsync`, add concurrency tokens, or touch the OTP throttle —
  that is phase 2.

## Commit

```
Centralise time on IDateTime.UtcNow

DateTimeService returned local time while the Serilog sink and the log
retention purge used UTC, leaving two tables in one database timestamped
on clocks that differ by the UTC offset. Local time is also not monotonic
across DST, which affects the OTP resend cooldown and latest-challenge
lookup.

IDateTime now exposes UtcNow only, DateTimeService is the one sanctioned
reader of the system clock, LogRetentionService goes through it, and a
BannedApiAnalyzers rule makes any other clock read a build error.
```
