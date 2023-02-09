using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationStatusCommandValidator : AbstractValidator<UpdateApplicationStatusCommand>
    {
        private readonly IUnitOfWork _unitOfWork;

        public UpdateApplicationStatusCommandValidator(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;

            RuleFor(a => a.Id).NotEmpty()
                .Must(LoanApplicationExists).WithMessage("Invalid application");
        }

        public bool LoanApplicationExists(int loanId)
        {
            return _unitOfWork.LoanApplicationRepository.GetById(loanId) != null;
        }
    }
}
