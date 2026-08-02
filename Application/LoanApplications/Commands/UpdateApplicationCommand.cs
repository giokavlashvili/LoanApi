using Application.Common.Extensions;
using Application.Common.Interfaces;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

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

            // UpdateApplicationCommandValidator already established that this id exists, but it ran
            // in ValidationBehavior — outside the transaction and before this load. A delete landing
            // in that window used to dereference null here and surface as a 500.
            //
            // DomainValidationException rather than ValidationException, deliberately: the latter is
            // ValidationBehavior's transport for FluentValidation failures, and no validator ran
            // here. Reusing the validator's own key means the caller reads the same localized text,
            // but note the shape differs — it arrives on ProblemDetails.detail rather than in
            // errors["Id"]. That is how every other handler-level guard in this codebase already
            // reports (RequireUserId, VerifiableOperationService.LoadOwnedAsync), and it is the
            // dominant 400 shape in this API; only validator failures populate errors.
            var entity = await _applications.GetByIdAsync(request.Id, cancellationToken)
                ?? throw new DomainValidationException("InvalidApplication");

            entity.Update(
                request.LoanTypeId,
                request.Amount,
                request.CurrencyId,
                request.PeriodPerMonth);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
