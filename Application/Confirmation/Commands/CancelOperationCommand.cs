using Application.Common.Extensions;
using Application.Common.Interfaces;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Confirmation.Commands
{
    /// <summary>
    /// Abandons a parked operation. Restricted to the initiator whatever the confirmation policy
    /// says: an approver declining to confirm is not the same act as the requester withdrawing,
    /// and letting an approver cancel would make a four-eyes operation cancellable by exactly the
    /// person it is meant to be checked by.
    /// </summary>
    public record CancelOperationCommand : IRequest
    {
        public Guid OperationId { get; set; }
    }

    public class CancelOperationCommandHandler : IRequestHandler<CancelOperationCommand>
    {
        private readonly IPendingOperationRepository _operations;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;

        public CancelOperationCommandHandler(
            IPendingOperationRepository operations,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            IDateTime dateTime)
        {
            _operations = operations;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _dateTime = dateTime;
        }

        public async Task Handle(CancelOperationCommand request, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.RequireUserId();

            var operation = await _operations.GetByOperationIdAsync(request.OperationId, cancellationToken)
                ?? throw new DomainValidationException("PendingOperationNotFound");

            if (!string.Equals(operation.InitiatedBy, userId, StringComparison.Ordinal))
                throw new DomainValidationException("PendingOperationWrongConfirmer");

            operation.Cancel(_dateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
