# Vertical slice reference paths

Copy the `LoanApplication` sample. Replace names for the new aggregate.

## Domain

| Artifact | Sample path |
|----------|-------------|
| Entity | `Domain/Entities/LoanApplication.cs` |
| Events | `Domain/Events/ApplicationCreatedEvent.cs` (and status/updated/deleted peers) |
| Enum | `Domain/Enums/LoanStatus.cs` |
| Repo interface | `Domain/Repositories/ILoanApplicationRepository.cs` |
| UoW | `Domain/Repositories/IUnitOfWork.cs` |

## Application

| Artifact | Sample path |
|----------|-------------|
| Feature root | `Application/LoanApplications/` |
| Command + handler | `Application/LoanApplications/Commands/CreateApplicationCommand.cs` |
| Query | `Application/LoanApplications/Queries/GetApplicationsQuery.cs` |
| DTO | `Application/LoanApplications/Dtos/LoanApplicationDto.cs` |
| Validator | `Application/LoanApplications/Validators/CreateApplicationCommandValidator.cs` |
| Event handler | `Application/LoanApplications/EventHandlers/` |

## Infrastructure

| Artifact | Sample path |
|----------|-------------|
| EF config | `Infrastructure/Persistence/Configurations/LoanApplicationConfiguration.cs` |
| Repository | `Infrastructure/Persistence/Repositories/LoanApplicationRepository.cs` |
| UoW impl | `Infrastructure/Persistence/Repositories/UnitOfWork.cs` |
| DbContext | `Infrastructure/Persistence/ApplicationDbContext.cs` |

## WebApi / i18n / tests

| Artifact | Sample path |
|----------|-------------|
| Controller | `WebApi/Controllers/LoanApplicationController.cs` |
| Localization | `WebApi/Resources/localization.json` |
| Domain tests | `Domain.UnitTests/Entities/LoanApplicationTests.cs` |
| App tests | `Application.UnitTests/LoanApplications/Commands/CreateLoanApplicationTests.cs` |

## OTP opt-in samples

| Pattern | Sample |
|---------|--------|
| Account phone | `UpdateApplicationStatusCommand` |
| Explicit recipient | `RegisterUserCommand` |
