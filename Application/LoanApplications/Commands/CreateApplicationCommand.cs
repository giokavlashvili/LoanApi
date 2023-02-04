using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Domain.Repositories;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private readonly IUnitOfWork _uow;
        public CreateApplicationCommandhandler(ICurrentUserService currentUserService, IDateTime dateTime, IUnitOfWork uow)
        {
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _uow = uow;
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

            entity.AddDomainEvent(new ApplicationCreatedEvent(entity));

            await _uow.LoanApplicationRepository.AddAsync(entity);
            await _uow.SaveAsync();

            return entity.Id;
        }
    }
}
