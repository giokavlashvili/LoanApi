using Application.Common.Confirmation;
using Application.Otp.Dtos;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Confirmation.Commands
{
    /// <summary>
    /// Texts a code for a parked operation to the caller's own number.
    /// <para>
    /// This is what makes four eyes work. A code is issued at initiate only when the policy names
    /// the initiator as the confirmer; under any other policy the approver is somebody the server
    /// has not met at that point, and texting the initiator would send the code to the one person
    /// who must not be able to complete the operation alone.
    /// </para>
    /// <para>
    /// Deliberately not itself confirmable — it is the way to obtain a code, so requiring one
    /// would deadlock the flow, exactly as with <c>ResendOtpCommand</c>.
    /// </para>
    /// </summary>
    public record RequestOperationChallengeCommand : IRequest<OtpChallengeDto>
    {
        public Guid OperationId { get; set; }
    }

    public class RequestOperationChallengeCommandHandler : IRequestHandler<RequestOperationChallengeCommand, OtpChallengeDto>
    {
        private readonly IPendingOperationRepository _operations;
        private readonly IOperationTypeRegistry _registry;
        private readonly IOperationConfirmationService _confirmationService;

        public RequestOperationChallengeCommandHandler(
            IPendingOperationRepository operations,
            IOperationTypeRegistry registry,
            IOperationConfirmationService confirmationService)
        {
            _operations = operations;
            _registry = registry;
            _confirmationService = confirmationService;
        }

        public async Task<OtpChallengeDto> Handle(RequestOperationChallengeCommand request, CancellationToken cancellationToken)
        {
            var operation = await _operations.GetByOperationIdAsync(request.OperationId, cancellationToken)
                ?? throw new DomainValidationException("PendingOperationNotFound");

            if (operation.Status != Domain.Enums.PendingOperationStatus.Pending)
                throw new DomainValidationException("PendingOperationAlreadyResolved");

            var descriptor = _registry.Find(operation.OperationType!)
                ?? throw new DomainValidationException("PendingOperationTypeUnknown");

            // Checks the policy before texting, so a caller who could never confirm this operation
            // cannot use it as a way to have messages sent to themselves.
            return await _confirmationService.IssueChallengeAsync(operation, descriptor, cancellationToken);
        }
    }
}
