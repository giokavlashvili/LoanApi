using Application.Common.Interfaces;
using Domain.Enums;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Application.LoanApplications.Commands
{
    public record UpdateApplicationStatusCommand : IRequest
    {
        public int Id { get; set; }
        public LoanStatus Status { get; set; }
    }

    public class UpdateApplicationStatusCommandHandler : IRequestHandler<UpdateApplicationStatusCommand>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IDateTime _dateTime;
        private readonly IUnitOfWork _uow;

        public UpdateApplicationStatusCommandHandler(ICurrentUserService currentUserService, IDateTime dateTime, IUnitOfWork uow)
        {
            _currentUserService = currentUserService;
            _dateTime = dateTime;
            _uow = uow;
        }

        public async Task<Unit> Handle(UpdateApplicationStatusCommand request, CancellationToken cancellationToken)
        {
            var entity = await _uow.LoanApplicationRepository.GetByIdAsync(request.Id);

            entity.UpdateStatus(
                request.Status,
                _currentUserService.UserId,
                _dateTime.Now);

            _uow.LoanApplicationRepository.Update(entity);

            await _uow.SaveAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
