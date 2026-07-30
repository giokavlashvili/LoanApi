using Application.Common.Extensions;
using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;
using MediatR;

namespace Application.LoanApplications.Commands
{
    public record CreateApplicationCommand : IRequest<int>
    {
        public int LoanTypeId { get; set; }
        public decimal Amount { get; set; }
        public int CurrencyId { get; set; }
        public int PeriodPerMonth { get; set; }
    }

    public class CreateApplicationCommandHandler : IRequestHandler<CreateApplicationCommand, int>
    {
        private readonly ILoanApplicationRepository _applications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;

        public CreateApplicationCommandHandler(
            ILoanApplicationRepository applications,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService)
        {
            _applications = applications;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
        }

        public async Task<int> Handle(CreateApplicationCommand request, CancellationToken cancellationToken)
        {
            // The audit columns are stamped by AuditableEntityInterceptor, so the id is not passed
            // to the factory — but it still has to exist, or the row would be written with a null
            // CreatedBy. Discarding the return value is deliberate: the guard is the point.
            _currentUserService.RequireUserId();

            var entity = LoanApplication.Create(
                request.LoanTypeId,
                request.Amount,
                request.CurrencyId,
                request.PeriodPerMonth);

            await _applications.AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return entity.Id;
        }
    }
}
