using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.LoanApplications.Commands
{
    public record CreateApplicationCommand : IRequest<int>
    {
        public int LoanTypeId { get; set; }
        public decimal Amount { get; set; }
        public int CurrencyId { get; set; }
        public int PeriodPerMonth { get; set; }
    }

    public class CreateApplicationCommandhandler : IRequestHandler<CreateApplicationCommand, int>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;
        private readonly IUnitOfWork _unitOfWork;
        public CreateApplicationCommandhandler(ICurrentUserService currentUserService, IDateTime dateTime, IUnitOfWork uow)
        {
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _unitOfWork = uow;
        }

        public async Task<int> Handle(CreateApplicationCommand request, CancellationToken cancellationToken)
        {
            var currentUserId = _currentUserService.UserId;

            var entity = LoanApplication.Create(
                request.LoanTypeId, 
                request.Amount, 
                request.CurrencyId, 
                request.PeriodPerMonth,
                currentUserId,
                _dateTime.Now);

            await _unitOfWork.LoanApplicationRepository.AddAsync(entity);

            await _unitOfWork.SaveAsync(cancellationToken);

            return entity.Id;
        }
    }
}
