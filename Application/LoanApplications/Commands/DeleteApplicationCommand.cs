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

            _applications.Remove(entity);

            entity.AddDomainEvent(new ApplicationDeletedEvent(entity));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
