using Domain.Repositories;
using MediatR;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

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

            // The aggregate raises its own deletion event, like every other event it has. The
            // handler used to construct ApplicationDeletedEvent itself.
            entity.Delete();

            _applications.Remove(entity);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
