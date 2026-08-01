using Application.Common.Operations;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Application.LoanApplications.Commands
{
    /// <summary>
    /// The worked example of a verified operation, and the counterpart to
    /// <see cref="UpdateApplicationStatusCommand"/>'s <c>IRequireOtpVerification</c>: same
    /// protection, different mechanism. The command itself is untouched — no <c>ChallengeId</c>,
    /// no <c>OtpCode</c>, no change to the handler.
    /// <para>
    /// Also the proof that a <strong>void</strong> command works here. MediatR.Contracts 2.x made
    /// <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> unrelated, so a registry checking only the
    /// generic one would reject exactly this.
    /// </para>
    /// <para>
    /// <strong>Its direct endpoint was removed.</strong> Registering an operation does not gate an
    /// existing route — leaving <c>DELETE .../DeleteApplication</c> in place would have made the
    /// confirmation optional, and therefore pointless. The only way in is now
    /// <c>Verification/Initiate</c> + <c>Verification/Confirm</c>.
    /// </para>
    /// </summary>
    [VerifiableOperation("DeleteLoanApplication")]
    public record DeleteApplicationCommand : IRequest
    {
        public int Id { get; set; }
    }

    public class DeleteApplicationCommandHandler : IRequestHandler<DeleteApplicationCommand>
    {
        private readonly ILoanApplicationRepository _applications;
        private readonly IUnitOfWork _unitOfWork;

        public DeleteApplicationCommandHandler(ILoanApplicationRepository applications, IUnitOfWork unitOfWork)
        {
            _applications = applications;
            _unitOfWork = unitOfWork;
        }

        public async Task Handle(DeleteApplicationCommand request, CancellationToken cancellationToken)
        {
            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken);

            // The aggregate raises its own deletion event, like every other event it has. The
            // handler used to construct ApplicationDeletedEvent itself.
            entity.Delete();

            _applications.Remove(entity);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
