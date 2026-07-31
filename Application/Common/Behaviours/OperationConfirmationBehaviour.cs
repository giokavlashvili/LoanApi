using Application.Common.Confirmation;
using Application.Common.Exceptions;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Application.Common.Behaviors
{
    /// <summary>
    /// Captures any request implementing <see cref="IRequireOperationConfirmation"/> instead of
    /// running it, and parks it until somebody confirms. Requests that do not implement it are
    /// passed straight through, so this costs one type check for everything else in the pipeline.
    /// <para>
    /// The counterpart to <see cref="OtpVerificationBehavior{TRequest, TResponse}"/>, and the
    /// choice between them is who holds the payload. There, the client keeps it and replays it
    /// with a code attached; here, the server stores it once, so the confirmation can come from a
    /// different user, a different device, or hours later.
    /// </para>
    /// </summary>
    // "notnull" rather than "TRequest : IRequest<TResponse>" — see ValidationBehavior for why the
    // tighter constraint silently excluded every void command. It matters especially here:
    // UpdateApplicationStatusCommand, the one confirmable command in the box, returns nothing.
    public class OperationConfirmationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly IOperationTypeRegistry _registry;
        private readonly IOperationExecutionContext _executionContext;
        private readonly IOperationConfirmationService _confirmationService;
        private readonly IPendingOperationRepository _operations;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;
        private readonly IOptionsMonitor<PendingOperationOptions> _optionsMonitor;
        private readonly ILogger<OperationConfirmationBehavior<TRequest, TResponse>> _logger;

        public OperationConfirmationBehavior(
            IOperationTypeRegistry registry,
            IOperationExecutionContext executionContext,
            IOperationConfirmationService confirmationService,
            IPendingOperationRepository operations,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            IDateTime dateTime,
            IOptionsMonitor<PendingOperationOptions> optionsMonitor,
            ILogger<OperationConfirmationBehavior<TRequest, TResponse>> logger)
        {
            _registry = registry;
            _executionContext = executionContext;
            _confirmationService = confirmationService;
            _operations = operations;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is not IRequireOperationConfirmation)
                return await next();

            // The replay of an already confirmed operation. The signal is ambient and is set only
            // by ConfirmOperationCommandHandler — never read it off the request, which is caller
            // controlled and would make this a one-field bypass of the entire mechanism.
            if (_executionContext.IsConfirmedExecution)
                return await next();

            var descriptor = _registry.Find(request.GetType())
                ?? throw new DomainValidationException("PendingOperationTypeUnknown");

            var options = _optionsMonitor.CurrentValue;
            var now = _dateTime.UtcNow;
            var userId = _currentUserService.RequireUserId();

            // Before anything is issued: a caller past their cap must not cost an SMS, and the
            // cap is worth nothing if it is only checked after the work is done.
            var pending = await _operations.CountPendingForUserAsync(userId, now, cancellationToken);

            if (pending >= options.MaxPendingPerUser)
                throw new DomainValidationException("TooManyPendingOperations");

            var operationId = Guid.NewGuid();

            var operation = PendingOperation.Create(
                operationId,
                descriptor.Discriminator,
                JsonSerializer.Serialize(request, request.GetType()),
                userId,
                now.Add(descriptor.Lifetime ?? options.DefaultLifetime),
                now);

            await _operations.AddAsync(operation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Only the initiator's own phone is knowable here. Under any other policy the
            // confirmer is somebody we have not met yet, so they ask for their own code once they
            // pick the operation up — see RequestOperationChallengeCommand.
            OtpChallengeDto? challenge = null;

            if (descriptor.Policy == ConfirmationPolicy.SameUser)
                challenge = await _confirmationService.IssueChallengeAsync(operation, descriptor, cancellationToken);

            _logger.LogInformation(
                "Operation {OperationId} parked for {OperationType} by {InitiatedBy}",
                operationId,
                descriptor.Discriminator,
                userId);

            throw new OperationConfirmationRequiredException(operationId, challenge);
        }
    }
}
