using Application.Common.Interfaces;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Operations.Dtos;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Operations.Services
{
    /// <summary>
    /// Drives the generic <c>initiate</c>/<c>confirm</c> flow. Policy, so it lives in
    /// <c>Application</c> — every collaborator is an abstraction, and the mechanisms it leans on
    /// (hashing, delivery, persistence) stay in <c>Infrastructure</c>, same split as
    /// <see cref="Otp.Services.OtpService"/>.
    /// </summary>
    public class VerifiableOperationService : IVerifiableOperationService
    {
        private readonly IVerifiableOperationRegistry _registry;
        private readonly IPendingOperationRepository _operations;
        private readonly IOtpService _otpService;
        private readonly IOtpCodeHasher _codeHasher;
        private readonly IOtpRecipientResolver _recipientResolver;
        private readonly ICurrentUserService _currentUserService;
        private readonly IIdentityService _identityService;
        private readonly IApplicationDbContext _dbContext;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDateTime _dateTime;
        private readonly IServiceProvider _services;
        private readonly ILogger<VerifiableOperationService> _logger;

        public VerifiableOperationService(
            IVerifiableOperationRegistry registry,
            IPendingOperationRepository operations,
            IOtpService otpService,
            IOtpCodeHasher codeHasher,
            IOtpRecipientResolver recipientResolver,
            ICurrentUserService currentUserService,
            IIdentityService identityService,
            IApplicationDbContext dbContext,
            IUnitOfWork unitOfWork,
            IDateTime dateTime,
            IServiceProvider services,
            ILogger<VerifiableOperationService> logger)
        {
            _registry = registry;
            _operations = operations;
            _otpService = otpService;
            _codeHasher = codeHasher;
            _recipientResolver = recipientResolver;
            _currentUserService = currentUserService;
            _identityService = identityService;
            _dbContext = dbContext;
            _unitOfWork = unitOfWork;
            _dateTime = dateTime;
            _services = services;
            _logger = logger;
        }

        public async Task<PendingOperationDto> InitiateAsync(
            string operationType,
            VerificationChannel channel,
            JsonElement payload,
            string? recipient = null,
            CancellationToken cancellationToken = default)
        {
            // Unknown operation is refused before anything is sent, never after.
            var descriptor = _registry.Get(operationType);

            await EnsureAuthorizedAsync(descriptor);

            // Rejected rather than ignored. An input the caller can fill in that is silently
            // discarded is worse than no input at all -- and here it would be a caller believing
            // they had redirected the code.
            if (!string.IsNullOrWhiteSpace(recipient) && !descriptor.AllowsCallerSuppliedRecipient)
                throw new DomainValidationException("OtpRecipientNotAllowed");

            var command = Bind(payload, descriptor);

            // Before the code is issued, per the standing rule: a rejection left until confirm
            // costs a message and a code, and the payload is frozen from here on.
            await ValidateAsync(command, descriptor, cancellationToken);

            var resolvedRecipient = await _recipientResolver.ResolveAsync(recipient, cancellationToken);
            var storedPayload = payload.GetRawText();

            // Not the request-swap guard it is on the gated path -- confirm carries no payload, so
            // there is nothing to swap. It detects a payload row edited directly in the database,
            // which is a real check precisely because the payload is not encrypted.
            var payloadHash = _codeHasher.HashRequest(storedPayload);

            var challenge = await _otpService.IssueAsync(
                descriptor.Name, resolvedRecipient, channel, _currentUserService.UserId, payloadHash, cancellationToken);

            var operation = PendingOperation.Create(
                Guid.NewGuid(),
                challenge.ChallengeId,
                descriptor.Name,
                storedPayload,
                _currentUserService.UserId,
                _dateTime.UtcNow);

            await _operations.AddAsync(operation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Verified operation {OperationId} initiated for {OperationType} on challenge {ChallengeId}",
                operation.OperationId, descriptor.Name, challenge.ChallengeId);

            return PendingOperationDto.From(operation.OperationId, challenge);
        }

        public async Task<OperationResultDto> ConfirmAsync(Guid operationId, string code, CancellationToken cancellationToken = default)
        {
            var operation = await LoadOwnedAsync(operationId, cancellationToken);

            // Replay guard, before the code is touched. A confirm whose response was lost must
            // return the same outcome rather than OtpAlreadyUsed -- the client otherwise cannot
            // tell whether the operation ran, which for anything moving money is the worst
            // available answer.
            switch (operation.Status)
            {
                case PendingOperationStatus.Succeeded:
                    return ToResult(operation);
                case PendingOperationStatus.Failed:
                    throw new DomainValidationException("PendingOperationAlreadyCompleted");
            }

            var descriptor = _registry.Get(operation.OperationType!);

            // Re-checked here, not only at initiate: minutes pass in between, which is long enough
            // for a role to be revoked.
            await EnsureAuthorizedAsync(descriptor);

            // OUTSIDE the transaction below, and that ordering is load-bearing. VerifyAsync
            // increments AttemptCount and saves it in a finally so a wrong code always counts;
            // inside a transaction that save only flushes, the throw rolls it back, MaxAttempts is
            // never reached and six digits become brute-forceable at leisure.
            await _otpService.VerifyAsync(
                operation.ChallengeId, operation.OperationType!, code, _codeHasher.HashRequest(operation.Payload!), cancellationToken);

            var command = await BindStoredAsync(operation, descriptor, cancellationToken);

            // Execution and the Succeeded write commit together, so a crash rolls back both and
            // Pending reliably means the work did not happen -- for handlers that stay inside the
            // database. One calling an external system cannot have that call rolled back and needs
            // its own idempotency.
            await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

            try
            {
                // The nested Send sees HasActiveTransaction and joins this one rather than opening
                // its own, which is what makes the two writes atomic.
                var result = await descriptor.Execute(command, _services, cancellationToken);

                operation.Succeed(Serialize(result), _dateTime.UtcNow);

                await _dbContext.CommitTransactionAsync(transaction, cancellationToken);
            }
            catch
            {
                _dbContext.RollbackTransaction();
                throw;
            }

            return ToResult(operation);
        }

        public async Task<PendingOperationDto> ResendAsync(Guid operationId, CancellationToken cancellationToken = default)
        {
            var operation = await LoadOwnedAsync(operationId, cancellationToken);

            if (operation.Status != PendingOperationStatus.Pending)
                throw new DomainValidationException("PendingOperationAlreadyCompleted");

            var challenge = await _otpService.ResendAsync(operation.ChallengeId, cancellationToken);

            // ResendAsync mints a *new* ChallengeId and retires the old one. Without moving the
            // operation onto it, every later confirm would verify against an invalidated challenge
            // and fail with OtpAlreadyUsed, permanently.
            operation.Rechallenge(challenge.ChallengeId, _dateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return PendingOperationDto.From(operation.OperationId, challenge);
        }

        /// <summary>
        /// A different principal gets the same error as an unknown id — a distinct "not yours"
        /// would confirm the operation exists.
        /// </summary>
        private async Task<PendingOperation> LoadOwnedAsync(Guid operationId, CancellationToken cancellationToken)
        {
            var operation = await _operations.GetByOperationIdAsync(operationId, cancellationToken)
                ?? throw new DomainValidationException("PendingOperationNotFound");

            if (!string.Equals(operation.UserId, _currentUserService.UserId, StringComparison.Ordinal))
                throw new DomainValidationException("PendingOperationNotFound");

            return operation;
        }

        private async Task EnsureAuthorizedAsync(VerifiableOperationDescriptor descriptor)
        {
            var userId = _currentUserService.UserId;

            if (descriptor.RequiresAuthentication && string.IsNullOrWhiteSpace(userId))
                throw new UnauthorizedAccessException();

            foreach (var policy in descriptor.RequiredPolicies)
            {
                if (!await _identityService.AuthorizeAsync(userId!, policy))
                    throw new Common.Exceptions.ForbiddenAccessException();
            }
        }

        /// <summary>
        /// Binds to <see cref="VerifiableOperationDescriptor.PayloadType"/> — the registry's type,
        /// never a type named in the request. Deserializing a caller-supplied type name is a
        /// remote code execution vector, and there is no version of this endpoint where that is
        /// acceptable.
        /// </summary>
        private static object Bind(JsonElement payload, VerifiableOperationDescriptor descriptor)
        {
            try
            {
                return payload.Deserialize(descriptor.PayloadType, SerializerOptions)
                    ?? throw new DomainValidationException("PendingOperationUnavailable");
            }
            catch (JsonException)
            {
                throw new DomainValidationException("PendingOperationUnavailable");
            }
        }

        /// <summary>
        /// Binds a payload read back off the row. Fails <em>closed</em>: a payload written before a
        /// deploy may not bind after one, and refusing is the safe direction — the alternative is
        /// executing a partially bound command. Same direction <c>HmacOtpCodeHasher.HashRequest</c>
        /// already fails in.
        /// </summary>
        private async Task<object> BindStoredAsync(PendingOperation operation, VerifiableOperationDescriptor descriptor, CancellationToken cancellationToken)
        {
            try
            {
                return JsonSerializer.Deserialize(operation.Payload!, descriptor.PayloadType, SerializerOptions)
                    ?? throw new JsonException("Payload deserialized to null.");
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogError(ex,
                    "Stored payload for operation {OperationId} could not be bound to {PayloadType}",
                    operation.OperationId, descriptor.PayloadType.Name);

                // Marked terminal so the caller is told to start over rather than retrying a
                // payload that will never bind. The code is already spent by this point.
                operation.Fail("PendingOperationUnavailable", _dateTime.UtcNow);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                throw new DomainValidationException("PendingOperationUnavailable");
            }
        }

        /// <summary>
        /// Resolves the payload's validator by its runtime type. This is why the service holds an
        /// <see cref="IServiceProvider"/> — the generic argument is only known per call, which is
        /// inherent to dispatching operations by name.
        /// </summary>
        private async Task ValidateAsync(object command, VerifiableOperationDescriptor descriptor, CancellationToken cancellationToken)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(descriptor.PayloadType);
            var validators = _services.GetServices(validatorType).Cast<IValidator>().ToList();

            if (validators.Count == 0)
                return;

            var context = new ValidationContext<object>(command);

            var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken))))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count > 0)
                throw new Common.Exceptions.ValidationException(failures);
        }

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private static string? Serialize(object? result) =>
            result is null ? null : JsonSerializer.Serialize(result, result.GetType(), SerializerOptions);

        private OperationResultDto ToResult(PendingOperation operation) => new()
        {
            OperationId = operation.OperationId,
            OperationType = operation.OperationType,
            Status = operation.Status,
            Result = operation.ResultPayload is null
                ? null
                : JsonDocument.Parse(operation.ResultPayload).RootElement.Clone()
        };
    }
}
