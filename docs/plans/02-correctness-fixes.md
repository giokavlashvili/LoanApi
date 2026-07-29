# Phase 2 — Correctness fixes

**Depends on:** phase 1. **EF migration:** yes (one). **Size:** medium.

Five independent defects. Each is a real runtime or data-integrity problem, not a style
preference. None is caught by the current test suite, because nothing exercises a real
database — every fix below therefore ships with the test that would have caught it.

Do them in order; task 5 needs the migration created in task 4.

---

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green, and phase 1 is committed (`IDateTime.UtcNow` exists). If `IDateTime` still exposes
`Now`, stop — run phase 1 first.

---

## Task 1 — `GetByIdAsync` passes the CancellationToken as a key value

**Severity: high. Delete, update and status-update are broken against a real database.**

`Infrastructure/Persistence/Repositories/Repository.cs` (~line 46):

```csharp
return await dbSet.FindAsync(id, cancellationToken);
```

`FindAsync` has two overloads: `FindAsync(params object?[]? keyValues)` and
`FindAsync(object?[]? keyValues, CancellationToken)`. An `int` is not an `object[]`, so the
second does not apply and the compiler binds the **params** overload — passing the id *and* the
boxed `CancellationToken` as two key values. EF throws at runtime:

> Entity type 'LoanApplication' is defined with a single key property, but 2 value(s) were
> passed to the 'Find' method.

Callers: `DeleteApplicationCommand.cs:27`, `UpdateApplicationCommand.cs:34`,
`UpdateApplicationStatusCommand.cs:45`. The unit tests mock `ILoanApplicationRepository`, so
this line never executes under test.

Fix:

```csharp
return await dbSet.FindAsync(new object[] { id }, cancellationToken);
```

Check `GetById(int id)` (the synchronous sibling, ~line 41) — `dbSet.Find(id)` is correct as-is.

### Test

`Repository<T>` cannot be tested against a mock `DbSet`. Add
`Infrastructure.UnitTests` — a new NUnit project referencing `Infrastructure` and
`Microsoft.EntityFrameworkCore.InMemory` (already a dependency of `Infrastructure`) — and add it
to `LoanApi.sln`. Build an `ApplicationDbContext` on the in-memory provider with a mocked
`IMediator`, seed one `LoanApplication`, and assert `GetByIdAsync` returns it.

This project is the home for the repository tests in tasks 3–5 too. Create it once, here.

---

## Task 2 — Blocking database calls inside async methods

**Severity: medium. Threadpool starvation under load.**

`Infrastructure/Identity/IdentityService.cs` uses synchronous LINQ against
`_userManager.Users` — which is an EF `IQueryable`, so each call blocks a threadpool thread on
a SQL round trip:

| Line | Current | Change to |
|---|---|---|
| ~122 (`IsInRoleAsync`) | `_userManager.Users.SingleOrDefault(...)` | `await _userManager.Users.SingleOrDefaultAsync(..., cancellationToken)` |
| ~129 (`AuthorizeAsync`) | same | same |
| ~145 (`DeleteUserAsync`) | same | same |

`AuthorizeAsync` is on the hot path for every policy check.

While in this file, also fix `GetUserNameAsync` (~line 53): it uses `.FirstAsync`, which
**throws** when the user is missing, followed by a `user?.UserName` null-check that can never
run. Use `FirstOrDefaultAsync` so the null-check means something.

Do not restructure anything else in this file — the `IConfiguration`/JWT work is phase 3, the
class split is phase 5.

### Test

Not directly unit-testable without a real `UserManager`. Verify by inspection: after the change
there must be **no** synchronous LINQ terminal operator (`SingleOrDefault`, `First`, `Any`,
`ToList`, `Count`) applied to `_userManager.Users` anywhere in the file.

---

## Task 3 — `Update()` on a tracked entity rewrites every column

**Severity: medium. Wasteful writes, and it silently clobbers concurrent field-level changes.**

The handlers load a tracked aggregate, mutate it, then call `Repository.Update(entity)` →
`dbSet.Update(entity)`, which sets the entry state to `Modified` and marks **all** properties
modified. `UpdateApplicationStatusCommand` changes only `Status`/`LastModified*` but emits an
UPDATE writing `Amount`, `CurrencyId`, `PeriodPerMonth`, `Created`, `CreatedBy` as well.

For an entity the change tracker is already tracking, the `Update` call is not just redundant —
it is harmful.

Fix — remove the redundant call in both handlers:

- `Application/LoanApplications/Commands/UpdateApplicationCommand.cs`
- `Application/LoanApplications/Commands/UpdateApplicationStatusCommand.cs`

```csharp
  var entity = await _unitOfWork.LoanApplicationRepository.GetByIdAsync(request.Id, cancellationToken);
  entity.UpdateStatus(request.Status, _currentUserService.UserId, _dateTime.UtcNow);
- _unitOfWork.LoanApplicationRepository.Update(entity);
  await _unitOfWork.SaveAsync(cancellationToken);
```

**Leave `OtpService` alone.** `OtpService.VerifyAsync` and `IssueAsync` also call `Update`, but
they are correct to: `OtpVerificationRepository` reads through `_context.OtpVerifications` with
tracking on, so removing the call there is safe *in principle* — but that code path is the
security-critical one and is changed again in phase 5. Do not touch it in this phase.

Keep `IRepository.Update` on the interface. It stays meaningful for detached entities, and
phase 4 decides its final fate.

### Test

`Application.UnitTests/LoanApplications/Commands/UpdateLoanApplicationTests.cs` has both a
`Setup(r => r.Update(...))` (~line 38) and a `Verify(r => r.Update(...))` (~line 64). Both must
be **removed**, not weakened. Replace with an assertion that `SaveAsync` was called and the
entity's state changed. Do the same in `UpdateApplicationStatus`'s test if it has the same pair.

Two things you will notice in these files — leave both alone for now, phase 4 handles them:

- they mock the **concrete** `UnitOfWork` (`new Mock<UnitOfWork>(...)`), which is the only
  reason `UnitOfWork.SaveAsync` and `Save` are declared `virtual`;
- they pass `It.IsAny<ICurrencyRepository>()` as *constructor arguments*. Outside a `Setup`,
  `It.IsAny<T>()` just returns `null`, which is what silently triggers `UnitOfWork`'s
  `?? new CurrencyRepository(...)` fallback. It is not doing what it looks like it is doing.

Add an `Infrastructure.UnitTests` test on the in-memory provider asserting the entity is
persisted after mutation **without** any `Update` call.

---

## Task 4 — No optimistic concurrency

**Severity: high for `LoanApplication`. Lost updates on the OTP-gated approval path.**

There is no concurrency token on any domain entity (the only `IsConcurrencyToken` in the model
snapshot is ASP.NET Identity's own `ConcurrencyStamp`).

`LoanApplication.UpdateStatus` guards against re-processing:

```csharp
if (Status == LoanStatus.Accepted || Status == LoanStatus.Rejected)
    throw new DomainValidationException("ApplicationAlreadyProcessed");
```

That check is in memory. Two concurrent approvals both read `Sent`, both pass, both write — the
invariant an SMS was spent to protect is not actually enforced. Same class of problem on
`OtpVerification.AttemptCount`: concurrent verify attempts can both read the same count, so the
attempt budget can be overspent.

### Changes

1. **Do not put `RowVersion` on `BaseEntity`.** `Currency` and `LoanType` derive from it too,
   and an unconfigured `byte[]` property maps as a `varbinary(max)` column on every one of them —
   junk columns and a migration nobody asked for. (`Log` is safe: it does **not** derive from
   `BaseEntity`; verify this before starting.)

   Declare it on the two entities that need it — `Domain/Entities/LoanApplication.cs` and
   `Domain/Entities/OtpVerification.cs`:

   ```csharp
   /// <summary>
   /// Optimistic concurrency token. Two concurrent writers to the same row make the second
   /// SaveChanges throw DbUpdateConcurrencyException instead of silently overwriting.
   /// </summary>
   public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
   ```

   If a third aggregate needs one later, introduce an `IHasConcurrencyToken` marker rather than
   pushing the property up into `BaseEntity`.

   No data annotation — configure it with the Fluent API, consistent with the rest.

2. Add to `Infrastructure/Persistence/Configurations/LoanApplicationConfiguration.cs` and
   `OtpVerificationConfiguration.cs`:

   ```csharp
   builder.Property(x => x.RowVersion).IsRowVersion();
   ```

   Do **not** add it to `CurrencyConfiguration` or `LoanTypeConfiguration` — they are reference
   data with no concurrent writers, and they no longer carry the property.

3. Translate the failure at the API edge. **The mapping does not live in the middleware.**
   `WebApi/Middlwares/UnhandledExceptionHandlerMiddlware.cs` is a blanket
   `catch (Exception) → 500` last resort; typed mapping is a dictionary of exception type →
   handler in `WebApi/Filters/ApiExceptionFilterAttribute.cs` (~line 23), registered via
   `options.Filters.Add<ApiExceptionFilterAttribute>()` in `WebApi/Extensions/ConfigureServices.cs`.

   Add a `DbUpdateConcurrencyException` entry to that dictionary plus its handler method,
   following the shape of the existing `HandleNotFoundException` / `HandleOtpRequiredException`,
   returning **409 Conflict** with a localized message. Add the key to
   `WebApi/Resources/localization.json` in the existing `{ "Key", "LocalizedValue": { "ka-GE",
   "en-US" } }` format — **both** locales.

   `WebApi` already references EF Core, so `DbUpdateConcurrencyException` is available in the
   filter. A concurrency failure surfacing as a 500 is a worse bug than the one being fixed.

4. Create the migration:

   ```powershell
   $env:SkipNSwag = "True"
   dotnet ef migrations add AddConcurrencyTokens --project Infrastructure --startup-project WebApi
   ```

   **Inspect the generated file before accepting it.** It should add `rowversion` columns and
   nothing else. If it contains unrelated changes, the model has drifted — stop and report.

### Test

In `Infrastructure.UnitTests`: the in-memory provider does **not** support `rowversion`, so this
cannot be covered there. Assert instead at the model level — build the `ApplicationDbContext`
model and verify `Model.FindEntityType(typeof(LoanApplication)).FindProperty("RowVersion")` is
`IsConcurrencyToken == true` and `ValueGenerated == OnAddOrUpdate`. Note in the report that true
concurrency behaviour needs an integration test against SQL Server, which this repo does not
have.

---

## Task 5 — The OTP throttle has a check-then-act race

**Severity: medium. The hourly SMS cap and the one-live-code rule can both be bypassed.**

`Infrastructure/Services/OtpService.cs`:

- `EnsureHourlyCapAsync` (~line 152) calls `CountRecentAsync`, compares, then inserts. Nothing
  holds a lock between the read and the write, so N concurrent requests all read the same count
  and all pass the cap. The cap is the control that stops the endpoint being used as an open SMS
  relay billed to whoever owns the provider account.
- `IssueAsync` (~line 61) reads the latest pending challenge, invalidates it, then inserts a new
  one. Two concurrent issues both invalidate the same predecessor and leave **two** live codes
  for one recipient — the exact thing the invalidate step exists to prevent.

Application-level checks cannot fix this. Push the invariant into the database.

### Changes

1. `Infrastructure/Persistence/Configurations/OtpVerificationConfiguration.cs` — add a unique
   filtered index so at most one `Pending` challenge can exist per recipient + purpose:

   ```csharp
   // At most one live challenge per recipient and purpose. The application also checks this
   // before inserting, but that read-then-write races: two concurrent issues both see no
   // predecessor and both insert. This is the only place the rule can actually be enforced.
   // 0 is OtpVerificationStatus.Pending.
   builder.HasIndex(o => new { o.Recipient, o.Purpose })
       .IsUnique()
       .HasFilter("[Status] = 0")
       .HasDatabaseName("UX_OtpVerifications_Recipient_Purpose_Pending");
   ```

   Verify `OtpVerificationStatus.Pending == 0` in `Domain/Enums/OtpVerificationStatus.cs` before
   writing the filter. If it is not 0, use the actual value and say so in the comment.

2. **Verify EF's statement ordering before trusting the index.** `IssueAsync` invalidates the
   previous challenge (UPDATE `Status` → `Expired`) and inserts a new `Pending` one in a *single*
   `SaveChangesAsync`. If EF emits the INSERT before the UPDATE, the unique index fires on a
   perfectly legitimate reissue and the OTP flow breaks.

   EF Core's `CommandBatchPreparer` orders commands, but the guarantee for same-table
   update-before-insert is not something to assume. Test it against real SQL Server: issue a
   challenge, then reissue for the same recipient and purpose, and confirm it succeeds. If it
   fails, split the operation into two saves (invalidate + save, then create + save) and add a
   comment explaining why the split exists. **Do not skip this check** — an in-memory test will
   not catch it, because the in-memory provider ignores unique indexes entirely.

3. `OtpService.IssueAsync` — the insert can now fail with `DbUpdateException` on the unique
   index when it loses the race. Catch it and translate to the throttle error the caller already
   understands:

   ```csharp
   catch (DbUpdateException) // lost the race to another concurrent issue
   {
       throw new DomainValidationException("OtpThrottled");
   }
   ```

   This catch survives phase 5's move of `OtpService` to `Application` unchanged:
   `Application.csproj` already references `Microsoft.EntityFrameworkCore` (it needs it for
   `DbSet<T>` on `IApplicationDbContext`), so `DbUpdateException` is available there too. No
   marker comment is needed.

   Note that the in-memory provider does not enforce unique indexes, so this path is
   SQL-Server-only. Do not add an in-memory test asserting the throw.

4. Keep the existing application-level `CountRecentAsync` check. It is still the mechanism for
   the hourly cap, and it produces a clean error in the common uncontended case. The index only
   backstops the race for the one-live-code rule.

5. Fold this into the **same migration** as task 4 if it has not been generated yet; otherwise
   create a second one, `AddOtpPendingUniqueIndex`.

### Existing data

The unique filtered index will fail to create if the database already contains more than one
`Pending` row for the same recipient + purpose. Before applying, retire the duplicates:

```sql
-- Keep the newest pending challenge per recipient+purpose, expire the rest (Status 2 = Expired;
-- verify against OtpVerificationStatus before running).
WITH ranked AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Recipient, Purpose ORDER BY Created DESC, Id DESC) AS rn
    FROM OtpVerifications WHERE Status = 0
)
UPDATE OtpVerifications SET Status = 2 WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);
```

Put this in the migration's `Up()` as a `migrationBuilder.Sql(...)` **before** the
`CreateIndex`, so a deploy against existing data does not fail.

---

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

If SQL Server at `localhost\SQLEXPRESS` is available, also run the API once
(`dotnet run --project WebApi`) so the Development startup path applies the migration, and
confirm it succeeds.

## Definition of done

- [ ] `GetByIdAsync` uses the `object[]` overload; covered by a test on the in-memory provider
- [ ] `Infrastructure.UnitTests` exists, is in `LoanApi.sln`, and passes
- [ ] No synchronous LINQ terminal operators on `_userManager.Users`; `GetUserNameAsync` uses
      `FirstOrDefaultAsync`
- [ ] Redundant `Update` calls removed from the two loan-application handlers; their tests no
      longer assert on `Update`
- [ ] `RowVersion` on `BaseEntity`, configured `IsRowVersion()` for `LoanApplication` and
      `OtpVerification`; `DbUpdateConcurrencyException` maps to 409 with a localized message
- [ ] Unique filtered index on pending OTP challenges, with the duplicate-retirement SQL ahead
      of it in the migration
- [ ] Migration(s) generated, inspected, and containing only the intended changes
- [ ] Build green, tests green

## Out of scope

- Do not reshape `IRepository`, `IUnitOfWork` or handler constructors — phase 4.
- Do not move `OtpService` or split `IdentityService` — phase 5.
- Do not add `IOptions<JwtOptions>` or thread `CancellationToken` everywhere — phase 3.
  (Exception: the three `IdentityService` methods in task 2 may gain a `CancellationToken`
  parameter if that is what it takes to call the async overloads cleanly.)

## Commit

```
Fix repository key lookup, blocking identity queries, and missing concurrency control

- Repository.GetByIdAsync bound the params overload of FindAsync and passed
  the CancellationToken as a second key value, breaking every delete, update
  and status-update against a real database.
- IdentityService ran synchronous LINQ against _userManager.Users on the
  authorization hot path.
- The loan handlers called DbSet.Update on already-tracked entities, marking
  every property modified and rewriting whole rows.
- No entity carried a concurrency token, so concurrent approvals both passed
  the already-processed guard.
- The OTP throttle checked then acted with nothing in between; a unique
  filtered index now enforces one live challenge per recipient and purpose.
```
