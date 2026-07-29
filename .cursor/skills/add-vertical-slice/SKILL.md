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
- [ ] 1. Domain entity + events + enum (if needed)
- [ ] 2. Repository interface + IUnitOfWork property
- [ ] 3. Application Commands/Queries/Dtos/Validators/EventHandlers
- [ ] 4. EF configuration + repository impl + UnitOfWork wiring
- [ ] 5. Controller action(s)
- [ ] 6. localization.json keys
- [ ] 7. Unit tests (Domain + Application)
- [ ] 8. EF migration (see ef-migration skill)
```

## 1. Domain

- `Domain/Entities/<Name>.cs`: `private set`, `static Create(...)`, mutators, `DomainValidationException("Key")`, `AddDomainEvent`.
- Events in `Domain/Events/`. Enums in `Domain/Enums/`.
- `Domain/Repositories/I<Name>Repository.cs` extending the generic pattern used by `ILoanApplicationRepository`.

## 2. Unit of work

- Add property to `IUnitOfWork` and `UnitOfWork`.
- Add optional ctor parameter on `UnitOfWork` for test injection.
- Update existing `Mock<UnitOfWork>(...)` call sites in unit tests.

## 3. Application slice

```
Application/<Feature>/
  Commands/   # record + handler same file
  Queries/
  Dtos/       # IMapFrom<T>
  Validators/ # AbstractValidator<T>, IUnitOfWork + IStringLocalizer
  EventHandlers/
```

- Handler: user/time → entity → `_unitOfWork.<Repo>` → `SaveAsync`.
- **No manual DI registration.**

## 4. Infrastructure

- `Persistence/Configurations/<Name>Configuration.cs` (`IEntityTypeConfiguration<T>`).
- `Persistence/Repositories/<Name>Repository.cs`.
- Wire into `UnitOfWork` / DI if not already convention-based for that repo type.

## 5. WebApi

- Controller: `ApiControllerBase`, `api/v1/[controller]`, `[Route(nameof(Action))]`, `[Authorize]` when protected.
- One-liner actions: `Mediator.Send(...)`.

## 6. Localization

- Add every new `DomainValidationException` / validator message key to `WebApi/Resources/localization.json` under `ka-GE` and `en-US`.

## 7. Tests

- Domain: create/update/invalid cases.
- Application: Moq handler tests verifying repository + `SaveAsync`.

## 8. Migration

Use the `ef-migration` skill. Do not hand-edit the model snapshot unless fixing a broken migration.

## Anti-patterns

- Business rules in handlers or controllers
- Manual MediatR/FluentValidation registration
- Putting repository interfaces in Application
- Forgetting UoW optional ctor + test mocks

## Additional resources

- Exact sample paths: [reference.md](reference.md)
- Deep guide: [`docs/architecture.md`](../../../docs/architecture.md)
