using Application.Common.Confirmation;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Domain.Enums;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Application.LoanApplications.Commands
{
    /// <summary>
    /// The sample of the <b>server-held</b> confirmation topology, and the one that shows two step
    /// verification is not an authentication feature: an ordinary business command opts in.
    /// <para>
    /// An approval is the case the inline <c>IRequireOtpVerification</c> gate cannot express. That
    /// gate leaves the payload with the client and has it replayed with a code attached, so only
    /// whoever composed the request can ever complete it. Here the server captures the command at
    /// initiate, which is what allows <see cref="ConfirmationPolicy.DifferentUser"/> — a second
    /// person approves, and they never handle the payload at all.
    /// </para>
    /// <para>
    /// Registration goes the other way deliberately: its command carries a password, and a parked
    /// operation persists its payload. Which topology to use is decided by what is in the payload
    /// and who has to confirm it, not by how sensitive the operation feels.
    /// </para>
    /// <para>
    /// Note there are no confirmation members on this command. The handle and the code go to
    /// <c>ConfirmOperationCommand</c>, and the confirmed-execution signal is ambient — a flag here
    /// would be caller controlled and would skip confirmation entirely.
    /// </para>
    /// </summary>
    [ConfirmableOperation("loan.status.update.v1", Policy = ConfirmationPolicy.DifferentUser)]
    public record UpdateApplicationStatusCommand : IRequest, IRequireOperationConfirmation
    {
        public int Id { get; set; }
        public LoanStatus Status { get; set; }
    }

    public class UpdateApplicationStatusCommandHandler : IRequestHandler<UpdateApplicationStatusCommand>
    {
        private readonly ILoanApplicationRepository _applications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;

        public UpdateApplicationStatusCommandHandler(
            ILoanApplicationRepository applications,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService)
        {
            _applications = applications;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
        }

        public async Task Handle(UpdateApplicationStatusCommand request, CancellationToken cancellationToken)
        {
            _currentUserService.RequireUserId();

            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken);

            entity.UpdateStatus(request.Status);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
