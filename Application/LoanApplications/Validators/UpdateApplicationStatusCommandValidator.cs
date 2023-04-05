using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationStatusCommandValidator : AbstractValidator<UpdateApplicationStatusCommand>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IStringLocalizer _stringLocalizer;

        public UpdateApplicationStatusCommandValidator(IUnitOfWork unitOfWork, IStringLocalizer stringLocalizer)
        {
            _unitOfWork = unitOfWork;
            _stringLocalizer = stringLocalizer;

            RuleFor(a => a.Id).NotEmpty()
                .Must(LoanApplicationExists).WithMessage(_stringLocalizer.GetString("InvalidApplication"));
        }

        public bool LoanApplicationExists(int loanId)
        {
            return _unitOfWork.LoanApplicationRepository.GetById(loanId) != null;
        }
    }
}
