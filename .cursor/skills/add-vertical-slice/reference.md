# Vertical slice reference paths

Copy the `LoanApplication` sample. Replace names for the new aggregate.

## Domain

| Artifact | Sample path |
|----------|-------------|
| Entity | `Domain/Entities/LoanApplication.cs` |
| Events | `Domain/Events/ApplicationCreatedEvent.cs` (and status/updated/deleted peers) |
| Enum | `Domain/Enums/LoanStatus.cs` |
| Aggregate root marker | `Domain/Common/IAggregateRoot.cs` |
| Repo interface | `Domain/Repositories/ILoanApplicationRepository.cs` (write-side lookups: `IOtpVerificationRepository.cs`) |
| Repo base | `Domain/Repositories/IRepository.cs` — generic surface; BCL types only, no EF |
| UoW | `Domain/Repositories/IUnitOfWork.cs` — `SaveChangesAsync` only, nothing to add per slice |

## Application

| Artifact | Sample path |
|----------|-------------|
| Feature root | `Application/LoanApplications/` |
| Command + handler | `Application/LoanApplications/Commands/CreateApplicationCommand.cs` (repository + `IUnitOfWork` injected directly; `RequireUserId()` guard, no audit arguments) |
| Query, paginated | `Application/LoanApplications/Queries/GetApplicationsQuery.cs` (`.Select(...)` + count from one `IQueryable`) |
| Query, flat list | `Application/Currencies/Queries/GetCurrenciesQuery.cs` |
| DTO | `Application/LoanApplications/Dtos/LoanApplicationDto.cs` (public setters — member-init projection) |
| Validator | `Application/LoanApplications/Validators/CreateApplicationCommandValidator.cs` (`IApplicationDbContext` + `MustAsync`) |
| Event handler | `Application/LoanApplications/EventHandlers/` |
| Non-domain notification | `Application/Authenticate/Notifications/UserRegisteredNotification.cs` + `NotificationHandlers/` |

## Infrastructure

| Artifact | Sample path |
|----------|-------------|
| EF config | `Infrastructure/Persistence/Configurations/LoanApplicationConfiguration.cs` (relationships live here — the entity has no annotations) |
| Audit stamping | `Infrastructure/Persistence/Interceptors/AuditableEntityInterceptor.cs` — nothing to add per slice |
| Repository | `Infrastructure/Persistence/Repositories/LoanApplicationRepository.cs` |
| Repository base | `Infrastructure/Persistence/Repositories/Repository.cs` |
| UoW impl | `Infrastructure/Persistence/Repositories/UnitOfWork.cs` |
| DI registration | `Infrastructure/Common/Extensions/ConfigureServices.cs` — the `AddScoped` repository lines |
| DbContext | `Infrastructure/Persistence/ApplicationDbContext.cs` |

## WebApi / i18n / tests

| Artifact | Sample path |
|----------|-------------|
| Controller | `WebApi/Controllers/LoanApplicationsController.cs` |
| Localization | `WebApi/Resources/localization.json` |
| Domain tests | `Domain.UnitTests/Entities/LoanApplicationTests.cs` |
| App tests | `Application.UnitTests/LoanApplications/Commands/CreateLoanApplicationTests.cs` |
| Projection tests | `Infrastructure.UnitTests/Queries/ProjectionQueryTests.cs` (real context; a mocked one cannot run a projection) |
| SQL-translation tests | `Infrastructure.UnitTests/Persistence/ProjectionSqlTranslationTests.cs` (`[Explicit]`, real SQL Server) |

## OTP opt-in samples

| Pattern | Sample |
|---------|--------|
| Account phone | `UpdateApplicationStatusCommand` |
| Explicit recipient | `RegisterUserCommand` |
