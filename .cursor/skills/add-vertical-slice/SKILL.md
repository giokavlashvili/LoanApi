---
name: add-vertical-slice
description: >-
  Scaffolds a new Clean Architecture vertical slice (entity through controller
  and tests) following this boilerplate's conventions. Use when adding a new
  feature, aggregate, entity, CRUD operation, or Application feature folder.
---

# Add vertical slice

Copy this checklist and complete in order. Mirror existing sample: `LoanApplication` + `Application/LoanApplications` + `LoanApplicationController`.

## Checklist

```
Progress:
- [ ] 1. Domain entity (marked IAggregateRoot) + events + enum (if needed)
- [ ] 2. Repository interface
- [ ] 3. Application Commands/Queries/Dtos/Validators/EventHandlers
- [ ] 4. EF configuration + repository impl + DI registration
- [ ] 5. Controller action(s)
- [ ] 6. localization.json keys
- [ ] 7. Unit tests (Domain + Application, + Infrastructure for projections)
- [ ] 8. EF migration (see ef-migration skill)
```

## 1. Domain

- `Domain/Entities/<Name>.cs`: `private set`, `static Create(...)`, mutators, `DomainValidationException("Key")`, `AddDomainEvent`.
- **No audit parameters** on the factory or mutators — `Create(int loanTypeId, decimal amount, ...)`,
  not `Create(..., string createdById, DateTime created)`. `AuditableEntityInterceptor` stamps
  `Created`/`CreatedBy`/`LastModified`/`LastModifiedBy` at the save boundary.
- **No EF annotations** — no `[ForeignKey]`, no `[NotMapped]`, no `[Column]`. Mapping is Fluent API
  in step 4.
- If the aggregate raises an event on deletion, give it a `Delete()` that raises it (see
  `LoanApplication.Delete`); the handler calls `entity.Delete()` then `repository.Remove(entity)`.
- Is it an **aggregate root**? Mark it `IAggregateRoot` only if it is the entry point to a
  consistency boundary — something with invariants worth protecting. Reference/lookup data is
  **not** a root: it gets no marker, no repository, and is read through query handlers. See
  `Currency` / `LoanType`.
- Events in `Domain/Events/`. Enums in `Domain/Enums/`.
- `Domain/Repositories/I<Name>Repository.cs` extending `IRepository<TAggregate>`, following
  `ILoanApplicationRepository`. The generic base already covers by-id, filtered/ordered/paged
  reads, composable `Query()`, and add/update/remove. Add a **named** method when it reveals
  intent the generic call would bury (see `IOtpVerificationRepository.GetLatestAsync`). Keep the
  interface BCL-only — no EF types.

## 2. Unit of work

**Nothing to do.** `IUnitOfWork` is `SaveChangesAsync` and nothing else — do not add a repository
property to it. Handlers inject the repositories they need; step 4 registers them.

## 3. Application slice

```
Application/<Feature>/
  Commands/   # record + handler same file
  Queries/
  Dtos/       # IMapFrom<T>
  Validators/ # AbstractValidator<T>, IApplicationDbContext + IStringLocalizer
  EventHandlers/
```

- Command handler: inject `I<Name>Repository` **plus** `IUnitOfWork`. Entity factory or mutator →
  repository → `SaveChangesAsync`. A loaded aggregate is tracked; do not look for an `Update` method.
- Writing an auditable entity? Call `_currentUserService.RequireUserId()` first (extension in
  `Application/Common/Extensions`). It throws `DomainValidationException("InvalidUser")` instead of
  letting the interceptor write a null `CreatedBy`. Inject `IDateTime` only when a *business rule*
  needs the time — never to feed an audit column.
- Query handler: inject `IApplicationDbContext` + `IMapper`, then
  `.AsNoTracking().ProjectTo<TDto>(_mapper.ConfigurationProvider)`. No repository.
- DTOs need **public setters** for `ProjectTo` to work — a `private set` compiles and then fails at
  runtime, because the projection is a member-init expression, not reflection.
- Validators: inject `IApplicationDbContext` + `IStringLocalizer`; existence checks via `MustAsync`/
  `CustomAsync` + `AnyAsync`/`FirstOrDefaultAsync` (honour `CancellationToken`). Pure format rules
  may stay synchronous.
- **No manual DI registration** for handlers, validators or DTO profiles.

## 4. Infrastructure

- `Persistence/Configurations/<Name>Configuration.cs` (`IEntityTypeConfiguration<T>`) — including
  relationships (`HasOne(...).WithMany().HasForeignKey(...)`) and any `Ignore`, since the entity
  carries no annotations.
- `Persistence/Repositories/<Name>Repository.cs`, deriving `Repository<TAggregate>`.
- Repositories are **not** assembly-scanned like handlers/validators — add an explicit
  `services.AddScoped<I<Name>Repository, <Name>Repository>();` line in `AddInfrastructureServices`
  (`Infrastructure/Common/Extensions/ConfigureServices.cs`), beside the existing ones. Nothing to
  wire into `UnitOfWork`.

## 5. WebApi

- Controller: `ApiControllerBase`, `api/v1/[controller]`, `[Route(nameof(Action))]`, `[Authorize]` when protected.
- One-liner actions: `Mediator.Send(...)`.

## 6. Localization

- Add every new `DomainValidationException` / validator message key to `WebApi/Resources/localization.json` under `ka-GE` and `en-US`.

## 7. Tests

- Domain: create/update/invalid cases.
- Application: Moq handler tests mocking `I<Name>Repository` + `IUnitOfWork` (the interfaces, not
  `UnitOfWork`); verify the repository call + `SaveChangesAsync`. Never pass `It.IsAny<T>()` as a
  constructor argument.
- Infrastructure: if the slice added a query handler, its `ProjectTo` needs a **real** context —
  a mocked `IApplicationDbContext` cannot execute a projection. See
  `Infrastructure.UnitTests/Queries/ProjectionQueryTests.cs`.

## 8. Migration

Use the `ef-migration` skill. Do not hand-edit the model snapshot unless fixing a broken migration.

## Anti-patterns

- Business rules in handlers or controllers
- Manual MediatR/FluentValidation registration
- Putting repository interfaces in Application
- Threading `createdById` / timestamps through a factory or mutator signature — the interceptor
  stamps them; drop the parameters and guard with `RequireUserId()` instead
- EF annotations on an entity, or a handler constructing the aggregate's own domain event
- Forgetting the repository's DI registration in `AddInfrastructureServices` — it will fail to resolve
- Adding a repository property to `IUnitOfWork`, or injecting `IUnitOfWork` to reach a repository
  through it: a handler's dependencies belong in its constructor
- A repository for reference/lookup data (no aggregate root, no repository)
- Passing includes as a string instead of a shaper (`include: q => q.Include(...)`)
- Calling `repository.Update(entity)` on an aggregate loaded through the same context — it is a
  no-op there by design; mutating the tracked aggregate is what persists
- EF types (`DbSet`, `IIncludableQueryable`) leaking into a `Domain/Repositories` interface
- `private set` on a DTO that gets projected
- Sync-over-async in a validator (`GetAwaiter().GetResult()` / `.Result` on EF or Identity) — use
  `MustAsync`/`CustomAsync` instead

## Additional resources

- Exact sample paths: [reference.md](reference.md)
- Deep guide: [`docs/architecture.md`](../../../docs/architecture.md)
