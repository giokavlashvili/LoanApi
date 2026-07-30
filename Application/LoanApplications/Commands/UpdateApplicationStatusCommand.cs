using Application.Common.Interfaces;
using Application.Common.Logging;
using Application.Common.Otp;
using Domain.Enums;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Application.LoanApplications.Commands
{
    /// <summary>
    /// The second sample of two step verification, and the one that shows it is not an
    /// authentication feature: an ordinary business command opts in the same way. Unlike
    /// registration it leaves <c>OtpRecipient</c> null, so the code goes to the number on the
    /// authenticated account rather than one the request chose.
    /// </summary>
    public record UpdateApplicationStatusCommand : IRequest, IRequireOtpVerification
    {
        public int Id { get; set; }
        public LoanStatus Status { get; set; }

        public Guid? ChallengeId { get; set; }

        [SensitiveData]
        public string? OtpCode { get; set; }
    }

    public class UpdateApplicationStatusCommandHandler : IRequestHandler<UpdateApplicationStatusCommand>
    {
        private readonly ILoanApplicationRepository _applications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;

        public UpdateApplicationStatusCommandHandler(
            ILoanApplicationRepository applications,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            IDateTime dateTime)
        {
            _applications = applications;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _dateTime = dateTime;
        }

        public async Task Handle(UpdateApplicationStatusCommand request, CancellationToken cancellationToken)
        {
            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken);

            entity.UpdateStatus(
                request.Status,
                _currentUserService.UserId,
                _dateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
