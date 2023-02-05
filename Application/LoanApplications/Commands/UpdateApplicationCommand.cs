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
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;
        private readonly IUnitOfWork _uow;

        public UpdateApplicationCommandHandler(ICurrentUserService currentUserService, IDateTime dateTime, IUnitOfWork uow)
        {
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _uow = uow;
        }

        public async Task<Unit> Handle(UpdateApplicationCommand request, CancellationToken cancellationToken)
        {
            var entity = await _uow.LoanApplicationRepository.GetByIdAsync(request.Id);

            entity.Update(
                request.LoanTypeId, 
                request.Amount, 
                request.CurrencyId, 
                request.PeriodPerMonth,
                _currentUserService.UserId, 
                _dateTime.Now);

            _uow.LoanApplicationRepository.Update(entity);

            await _uow.SaveAsync();

            return Unit.Value;
        }
    }
}
