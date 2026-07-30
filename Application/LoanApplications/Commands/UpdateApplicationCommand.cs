using Application.Common.Extensions;
using Application.Common.Interfaces;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

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

        public UpdateApplicationCommandHandler(
            ILoanApplicationRepository applications,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService)
        {
            _applications = applications;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
        }

        public async Task Handle(UpdateApplicationCommand request, CancellationToken cancellationToken)
        {
            _currentUserService.RequireUserId();

            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken);

            entity.Update(
                request.LoanTypeId,
                request.Amount,
                request.CurrencyId,
                request.PeriodPerMonth);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
