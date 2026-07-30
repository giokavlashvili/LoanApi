using Application.Common.Interfaces;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.LoanApplications.Commands
{
    public record UpdateApplicationCommand : IRequest
    {
        public int Id { get; set; }
        public int LoanTypeId { get; set; }
        public decimal Amount { get; set; }
        public int CurrencyId { get; set; }
        public int PeriodPerMonth { get; set; }
    }

    public class UpdateApplicationCommandHandler : IRequestHandler<UpdateApplicationCommand>
    {
        private readonly ILoanApplicationRepository _applications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;

        public UpdateApplicationCommandHandler(
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

        public async Task Handle(UpdateApplicationCommand request, CancellationToken cancellationToken)
        {
            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken);

            entity.Update(
                request.LoanTypeId,
                request.Amount,
                request.CurrencyId,
                request.PeriodPerMonth,
                _currentUserService.UserId,
                _dateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
