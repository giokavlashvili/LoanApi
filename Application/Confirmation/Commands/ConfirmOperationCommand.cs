using Application.Common.Confirmation;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Logging;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using System.Text.Json;

namespace Application.Confirmation.Commands
{
    /// <summary>
    /// Redeems a parked operation. One command for every confirmable operation — the payload is
    /// already on the server, so the confirmer supplies only the handle and the code.
    /// </summary>
    public record ConfirmOperationCommand : IRequest
    {
        public Guid OperationId { get; set; }

        /// <summary>
        /// The challenge being answered, as returned when the code was issued.
        /// <para>
        /// Carried by the caller rather than stored on the operation because
        /// <c>IOtpService.ResendAsync</c> mints a <b>new</b> challenge id and retires the old one:
        /// a stored id would be stale the moment anyone asked for a fresh message, and keeping it
        /// current would mean teaching the OTP layer about pending operations. Passing it costs
        /// nothing — a challenge issued for another operation carries a different request hash and
        /// is refused below.
        /// </para>
        /// </summary>
        public Guid ChallengeId { get; set; }

        [SensitiveData]
        public string? OtpCode { get; set; }
    }

    public class ConfirmOperationCommandHandler : IRequestHandler<ConfirmOperationCommand>
    {
        private readonly IPendingOperationRepository _operations;
        private readonly IOperationTypeRegistry _registry;
        private readonly IOperationConfirmationService _confirmationService;
        private readonly IOperationExecutionContext _executionContext;
        private readonly IOtpService _otpService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;
        private readonly ISender _sender;

        public ConfirmOperationCommandHandler(
            IPendingOperationRepository operations,
            IOperationTypeRegistry registry,
            IOperationConfirmationService confirmationService,
            IOperationExecutionContext executionContext,
            IOtpService otpService,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            IDateTime dateTime,
            ISender sender)
        {
            _operations = operations;
            _registry = registry;
            _confirmationService = confirmationService;
            _executionContext = executionContext;
            _otpService = otpService;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _sender = sender;
        }

        /// <remarks>
        /// The order of these steps is the correctness of the whole mechanism, and each one is
        /// placed where it is for a reason given inline. Do not reorder them.
        /// <para>
        /// Note that marking the operation confirmed and executing it are two transactions. If
        /// execution fails the operation is still spent and the initiator starts over. That is the
        /// safe direction — an operation never executes twice and never executes unconfirmed — and
        /// it cannot currently be improved on: domain events dispatch before SaveChanges with no
        /// outbox, so a rollback would unpublish nothing.
        /// </para>
        /// </remarks>
        public async Task Handle(ConfirmOperationCommand request, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.RequireUserId();

            var operation = await _operations.GetByOperationIdAsync(request.OperationId, cancellationToken)
                ?? throw new DomainValidationException("PendingOperationNotFound");

            // Fail closed on a payload captured under a discriminator that no longer exists. The
            // alternative — deserializing into whatever the type looks like now — is how a parked
            // command silently executes with a default for a field that was added after it parked.
            var descriptor = _registry.Find(operation.OperationType!)
                ?? throw new DomainValidationException("PendingOperationTypeUnknown");

            _confirmationService.EnsureMayConfirm(operation, descriptor, userId);

            await _otpService.VerifyAsync(
                request.ChallengeId,
                descriptor.Discriminator,
                request.OtpCode!,
                _confirmationService.ComputeRequestHash(operation.OperationId, descriptor.Discriminator),
                cancellationToken);

            // Committed before the command runs, so a crash midway through execution cannot leave
            // the operation replayable.
            operation.Confirm(userId, _dateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var command = JsonSerializer.Deserialize(operation.Payload!, descriptor.CommandType)
                ?? throw new DomainValidationException("InvalidOperationPayload");

            // Back through the full pipeline, so the captured command is validated again against
            // state that may have moved since it was parked. The ambient flag is what tells this
            // behaviour to let it past rather than parking it a second time.
            using (_executionContext.BeginConfirmedExecution(operation.OperationId))
            {
                await _sender.Send(command, cancellationToken);
            }
        }
    }
}
