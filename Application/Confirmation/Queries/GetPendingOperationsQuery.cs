using Application.Common.Confirmation;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Confirmation.Dtos;
using Domain.Repositories;
using MediatR;

namespace Application.Confirmation.Queries
{
    /// <summary>
    /// The caller's own unresolved operations. Scoped to the authenticated user rather than taking
    /// a user id, so it cannot be used to enumerate what somebody else has parked.
    /// </summary>
    public record GetPendingOperationsQuery : IRequest<IReadOnlyList<PendingOperationDto>>;

    public class GetPendingOperationsQueryHandler : IRequestHandler<GetPendingOperationsQuery, IReadOnlyList<PendingOperationDto>>
    {
        private readonly IPendingOperationRepository _operations;
        private readonly IOperationTypeRegistry _registry;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;

        public GetPendingOperationsQueryHandler(
            IPendingOperationRepository operations,
            IOperationTypeRegistry registry,
            ICurrentUserService currentUserService,
            IDateTime dateTime)
        {
            _operations = operations;
            _registry = registry;
            _currentUserService = currentUserService;
            _dateTime = dateTime;
        }

        public async Task<IReadOnlyList<PendingOperationDto>> Handle(GetPendingOperationsQuery request, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.RequireUserId();

            var operations = await _operations.ListPendingForUserAsync(userId, _dateTime.UtcNow, cancellationToken);

            return operations.Select(o => new PendingOperationDto
            {
                OperationId = o.OperationId,
                OperationType = o.OperationType,
                // Resolved rather than stored, so a policy change takes effect for rows already
                // parked instead of leaving them on whatever was declared when they were created.
                Policy = _registry.Find(o.OperationType!)?.Policy ?? ConfirmationPolicy.SameUser,
                Status = o.Status,
                InitiatedBy = o.InitiatedBy,
                Created = o.Created,
                ExpiresAt = o.ExpiresAt
            }).ToList();
        }
    }
}
