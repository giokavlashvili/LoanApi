# Phase 4 — Aggregate-scoped repositories and a slim unit of work

**Depends on:** phase 3. **EF migration:** no. **Size:** large. **Optional / structural.**

> This phase changes an established convention. It is worth doing, but it is a considered
> architectural choice, not a bug fix — if the goal is only to stabilise the template, phases
> 1–3 are sufficient and you can stop there. Do not start this phase without the earlier ones
> committed.

## Why

Three problems, all consequences of the same design:

**1. `IRepository<T>` is an EF wrapper living in `Domain`.**

```csharp
IEnumerable<TEntity> Get(
    Expression<Func<TEntity, bool>>? filter = null,
    Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
    string includeProperties = "");
```

`includeProperties` is a comma-separated magic string — a typo silently returns unloaded
navigations rather than failing, and no rename or find-usages will ever touch it.
`Expression<Func<T,bool>>` makes the domain depend on a query-provider concept. `Update` /
`UpdateRange` are EF verbs with no meaning in the ubiquitous language. In DDD a repository is
scoped to an **aggregate root**, returns aggregates, and exposes intention-revealing methods —
which the per-aggregate repositories here already do well. It is the generic base that undoes
it.

**2. `IUnitOfWork` is a service-locator registry.** Every handler injects it and reaches through
it (`_unitOfWork.LoanApplicationRepository.…`), so a handler's real dependencies are invisible
from its constructor. The cost is already visible in the tests: all four command test files pass
`It.IsAny<ICurrencyRepository>(), It.IsAny<ILoanTypeRepository>(), …` for repositories the
handler never touches. Adding one aggregate means editing `IUnitOfWork`, `UnitOfWork`, and every
test that mocks it.

**3. Queries pay for the write model.** `GetCurrenciesQuery` materialises full `Currency`
entities and AutoMapper-maps them to DTOs — every column fetched, change tracker consulted.
`GetApplicationsQuery` makes two round trips (`GetCountAsync` then `GetPaginatedListAsync`)
outside any snapshot, so the count and the page can disagree under concurrent writes. This is
CQRS; the read side has no invariants to protect and should not go through the write model at
all.

## Precondition

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Both green; phases 1–3 committed. Two parts of phase 3 are hard prerequisites:

- **Task 3** (repositories registered in DI) — or step 3 below has nothing to inject.
- **Task 7** (one AutoMapper version solution-wide) — this phase moves the read side onto
  `ProjectTo`, and while the app runs AutoMapper 14.0.0 and the tests run 15.1.0, a green test
  proves nothing about the deployed behaviour. Confirm before starting:

  ```bash
  grep -rn "AutoMapper" --include=*.csproj . | grep -v worktrees
  ```

  Every line must show the same version.

---

## Step 1 — Mark aggregate roots

Create `Domain/Common/IAggregateRoot.cs`:

```csharp
namespace Domain.Common
{
    /// <summary>
    /// Marks an entity as the entry point to an aggregate — the only kind of object a
    /// repository may load or persist. Entities inside an aggregate are reached through their
    /// root, never fetched independently, so the root can enforce the invariants that span them.
    /// </summary>
    public interface IAggregateRoot;
}
```

Apply to `LoanApplication` and `OtpVerification` only.

Deliberately **not** aggregate roots:

- `Currency`, `LoanType` — reference data. They are read through queries and seeded by
  `ApplicationDbContextInitialiser`; they have no behaviour and no invariants.
- `Log` — not a domain concept at all. Rows are written by the Serilog sink; EF owns only the
  schema.

## Step 2 — Reduce `IRepository<T>` to the write model

Replace `Domain/Repositories/IRepository.cs` with:

```csharp
using Domain.Common;

namespace Domain.Repositories
{
    /// <summary>
    /// Loads and persists whole aggregates. Deliberately small: querying is the read model's
    /// job (see the query handlers, which project straight to DTOs), and mutation happens
    /// through the aggregate's own methods, not through property assignment plus Update.
    /// </summary>
    public interface IRepository<TAggregate> where TAggregate : BaseEntity, IAggregateRoot
    {
        Task<TAggregate?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task AddAsync(TAggregate entity, CancellationToken cancellationToken = default);
        void Remove(TAggregate entity);
    }
}
```

Everything else goes: `Get`, `GetAsync`, `GetAll`, `GetAllAsync`, `GetById` (sync),
`Add`/`AddRange`/`AddRangeAsync`, `RemoveRange`, `Update`, `UpdateRange`.

Trim `Infrastructure/Persistence/Repositories/Repository.cs` to match. `Update` disappears
because every mutation path in this codebase loads a tracked aggregate — the change tracker
already knows (phase 2 task 3 removed the redundant calls). **Before deleting `Update`, confirm
no caller remains**: `OtpService` still calls it in two places (`IssueAsync` on the invalidated
predecessor, `VerifyAsync` inside the `finally`); convert both to plain mutation followed by the
existing save. Re-read the comment above the `finally` first — the save has to stay inside it, or
a failed attempt stops burning the attempt budget.

### Reference data after this change

`ICurrencyRepository` and `ILoanTypeRepository` become empty interfaces over non-aggregates.
**Delete both**, along with `CurrencyRepository` and `LoanTypeRepository` and their DI
registrations. Their only consumers are `GetCurrenciesQuery` and `GetLoanTypesQuery`, which
step 4 moves onto `IApplicationDbContext` anyway.

## Step 3 — Reduce `IUnitOfWork` to a save boundary

`Domain/Repositories/IUnitOfWork.cs`:

```csharp
namespace Domain.Repositories
{
    /// <summary>
    /// One transactional boundary per request. Repositories are injected directly into the
    /// handlers that use them — this exists only to commit.
    /// </summary>
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
```

Rename `SaveAsync` → `SaveChangesAsync` (it wraps `DbContext.SaveChangesAsync`; the name should
say so) and drop the synchronous `Save()` — nothing calls it, and a sync save on a request path
is not something to keep available.

`UnitOfWork` becomes a two-field class over `IApplicationDbContext`. All four repository
properties go.

## Step 4 — Rework the handlers

**Command handlers** inject the repositories they actually use, plus `IUnitOfWork`:

```csharp
public CreateApplicationCommandHandler(
    ILoanApplicationRepository applications,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IDateTime dateTime)
```

Files: `CreateApplicationCommand.cs`, `UpdateApplicationCommand.cs`,
`UpdateApplicationStatusCommand.cs`, `DeleteApplicationCommand.cs`.

While in `CreateApplicationCommand.cs`, fix the typo in the class name —
`CreateApplicationCommandhandler` → `CreateApplicationCommandHandler` (lowercase `h`). Update
its test.

### Prerequisite: make the DTO projectable

**Do this before writing any `ProjectTo` call, or the rewrite will not work.**

`Application/LoanApplications/Dtos/LoanApplicationDto.cs` declares `Amount`, `PeriodPerMonth`,
`Status`, `LoanType` and `Currency` with **`private set`**. In-memory `IMapper.Map` sets those by
reflection, which is why the current code works. `ProjectTo` does not: it builds a member-init
expression tree (`new LoanApplicationDto { Amount = …, … }`), and an object initializer cannot
assign an inaccessible setter.

Change those five to `public set` (`Id` and `Created` already are). `CurrencyDto` and
`LoanTypeDto` are already fine — public setters throughout, no change needed.

The two `MapFrom` expressions in `LoanApplicationDto.Mapping` are pure conditional expressions
over navigations, so they translate to SQL correctly and need no change. If any *future* DTO adds
a `MapFrom` containing a method call, `ProjectTo` will throw — map it explicitly in the profile
rather than falling back to entity materialisation.

### The rewrite

**Query handlers** stop using repositories entirely. Inject `IApplicationDbContext` and project:

```csharp
public async Task<List<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
    => await _context.Currencies
        .AsNoTracking()
        .ProjectTo<CurrencyDto>(_mapper.ConfigurationProvider)
        .ToListAsync(cancellationToken);
```

`ProjectTo` is `AutoMapper.QueryableExtensions`; AutoMapper 14 is already referenced. The
generated SQL then selects only the DTO's columns.

`GetApplicationsQuery` needs the count and the page from **one** `IQueryable`:

```csharp
var query = _context.LoanApplications.AsNoTracking().OrderByDescending(a => a.Created).ThenByDescending(a => a.Id);
var totalCount = await query.CountAsync(cancellationToken);
var items = await query.Skip((request.PageNumber - 1) * request.PageSize)
                       .Take(request.PageSize)
                       .ProjectTo<LoanApplicationDto>(_mapper.ConfigurationProvider)
                       .ToListAsync(cancellationToken);
```

`ProjectTo` replaces the manual `Include(a => a.Currency).Include(a => a.LoanType)` — the
projection pulls exactly the joined columns the DTO needs.

Two round trips remain (count + page) — that is normal and correct for pagination. What the
change fixes is that both now derive from one query definition instead of two repository methods
that could drift.

With this done, `ILoanApplicationRepository.GetCountAsync` and `GetPaginatedListAsync` have no
callers. Delete them; `ILoanApplicationRepository` keeps only what the write side needs.

## Step 5 — `OtpService`

`OtpService` uses `_unitOfWork.OtpVerificationRepository` and `_unitOfWork.SaveAsync`. Convert
it to inject `IOtpVerificationRepository` and `IUnitOfWork` directly, and rename the save call.

`IOtpVerificationRepository` keeps `GetByChallengeIdAsync`, `CountRecentAsync` and
`GetLatestAsync` — these are genuine write-side lookups (they feed decisions that mutate the
aggregate), so they belong on the repository, not in a query handler.

Preserve exactly, and re-read the comments explaining why before touching either:

- the `try/finally` in `VerifyAsync`, so a failed attempt still persists the incremented
  `AttemptCount`;
- the ordering in `IssueAsync` — save first, send the SMS after.

## Step 6 — Tests

- All four command test files: drop the `Mock<UnitOfWork>` with its four `It.IsAny<…>()`
  arguments; inject the one repository mock the handler needs plus a `Mock<IUnitOfWork>`. These
  files get substantially shorter — that shortening is the point of the phase.

  Two consequences worth knowing. The tests currently mock the **concrete** `UnitOfWork`, which
  is the only reason `SaveAsync` and `Save` are declared `virtual` — once they mock the
  interface, **drop `virtual`** from `UnitOfWork.SaveChangesAsync`. And the `It.IsAny<T>()`
  values passed as *constructor arguments* were never doing anything: outside a `Setup` they
  return `null`, which is precisely what triggered the `?? new CurrencyRepository(…)` fallback
  this phase deletes. Do not carry that idiom into the rewritten tests.
- `OtpServiceTests`: same change; keep every behavioural assertion.
- Query handlers now need a real `DbContext` to exercise `ProjectTo`. Put those tests in
  `Infrastructure.UnitTests` (created in phase 2) against the in-memory provider, and assert
  both the DTO contents and the pagination arithmetic. **A `Mock<IApplicationDbContext>` cannot
  test a projection** — do not try.

## Step 7 — Documentation

This phase invalidates the current guidance. Update:

- `.cursor/rules/00-project-core.mdc` — repo/UoW abstractions description
- `.cursor/rules/application-cqrs.mdc` — the handler shape line
  ("current user / `IDateTime` → entity factory/mutator → `_unitOfWork` → `SaveAsync`") is now
  wrong in two ways: the save method is renamed, and repositories are injected directly. Add the
  command-vs-query split: commands go through repositories, queries project from
  `IApplicationDbContext`.
- `.cursor/rules/infrastructure-ef.mdc` — the reduced `IRepository` surface
- `.cursor/rules/domain-entities.mdc` — `IAggregateRoot`, and which entities are roots
- `.cursor/rules/testing.mdc` — mock the repository, not the unit of work; projection tests need
  a real context
- `.cursor/skills/add-vertical-slice/{SKILL.md,reference.md}` — **the critical one.** The recipe
  currently says to add a repository property to `IUnitOfWork` and `UnitOfWork`. That step is now
  wrong and must be replaced with: mark the root, add the repository interface + implementation,
  register it in DI, inject it into the command handler, project in the query handler.
- `docs/architecture.md` — the persistence and CQRS sections

## Verification

```bash
dotnet build LoanApi.sln -p:SkipNSwag=True
```

```bash
dotnet test LoanApi.sln -p:SkipNSwag=True
```

Behavioural check — every controller endpoint must still return what it did before. If SQL
Server is available, run the API and exercise the loan application list, create, update, status
update and delete endpoints through Swagger. The projection rewrite is the highest-risk change
in this phase; a silently empty `Currency`/`LoanType` in the list response is the failure to
watch for.

## Definition of done

- [ ] `IAggregateRoot` exists; `LoanApplication` and `OtpVerification` implement it
- [ ] `IRepository<T>` is three members, constrained to aggregate roots
- [ ] `ICurrencyRepository` / `ILoanTypeRepository` and their implementations are deleted
- [ ] `IUnitOfWork` is `SaveChangesAsync` only, no repository properties, no sync `Save`
- [ ] Command handlers inject their repositories directly
- [ ] `LoanApplicationDto`'s five private setters made public **before** the projection rewrite
- [ ] Query handlers use `IApplicationDbContext` + `ProjectTo`, no repositories
- [ ] `virtual` removed from `UnitOfWork.SaveChangesAsync`; no `It.IsAny<T>()` as a constructor
      argument anywhere in the rewritten tests
- [ ] `CreateApplicationCommandHandler` typo fixed
- [ ] Tests no longer pass `It.IsAny<…>()` for unused repositories; projection tests run against
      a real context
- [ ] All six documentation files updated, `add-vertical-slice` in particular
- [ ] Build green, tests green, endpoints verified by hand if a database is available

## Out of scope

- Do not move `OtpService` between assemblies — phase 5.
- Do not add the audit interceptor or change entity factory signatures — phase 5.
- Do not introduce a specification pattern, `IQueryable`-returning repositories, or a separate
  read-model assembly. The projection approach above is the intended end state.

## Commit

```
Scope repositories to aggregate roots and slim the unit of work

IRepository<T> was an EF wrapper in the Domain layer — expression filters, a
comma-separated includeProperties string, and Update/UpdateRange, none of
which belong in the ubiquitous language. IUnitOfWork was a registry every
handler reached through, hiding real dependencies and forcing every test to
stub repositories it never used.

Repositories are now scoped to aggregate roots and injected directly into the
command handlers that use them; IUnitOfWork is the transactional boundary and
nothing else. Query handlers project straight from IApplicationDbContext with
ProjectTo, so the read side no longer materialises entities to throw them away.
```
