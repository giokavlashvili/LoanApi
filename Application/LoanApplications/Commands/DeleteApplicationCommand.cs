using Application.Common.Interfaces;
using Domain.Events;
using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.LoanApplications.Commands
{
    public record DeleteApplicationCommand : IRequest
    {
        public int Id { get; set; }
    }

    public class DeleteApplicationCommandHandler : IRequestHandler<DeleteApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;

        public DeleteApplicationCommandHandler(IUnitOfWork uow)
        {
            _unitOfWork = uow;
        }

        public async Task<Unit> Handle(DeleteApplicationCommand request, CancellationToken cancellationToken)
        {
            var entity = await _unitOfWork.LoanApplicationRepository.GetByIdAsync(request.Id);

            _unitOfWork.LoanApplicationRepository.Remove(entity);

            entity.AddDomainEvent(new ApplicationDeletedEvent(entity));

            await _unitOfWork.SaveAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
