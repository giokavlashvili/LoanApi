using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IStringLocalizer _stringLocalizer;

        public CreateApplicationCommandValidator(IUnitOfWork unitOfWork, IStringLocalizer stringLocalizer)
        {
            _unitOfWork = unitOfWork;
            _stringLocalizer = stringLocalizer;
            RuleFor(a => a.CurrencyId).Must(CurrencyExists).WithMessage(_stringLocalizer.GetString("InvalidCurrency"));

            RuleFor(a => a.LoanTypeId).Must(LoanTypeExists).WithMessage(_stringLocalizer.GetString("InvalidLoanType"));
        }

        public bool CurrencyExists(int currencyId)
        {
            return _unitOfWork.CurrencyRepository.GetById(currencyId) != null;
        }

        public bool LoanTypeExists(int loanTypeId)
        {
            return _unitOfWork.LoanTypeRepository.GetById(loanTypeId) != null;
        }
    }
}
