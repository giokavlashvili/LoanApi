using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;

namespace Application.LoanApplications.Validators
{
    public class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;

        public CreateApplicationCommandValidator(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;

            RuleFor(a => a.CurrencyId).Must(CurrencyExists).WithMessage("Invalid currency");

            RuleFor(a => a.LoanTypeId).Must(LoanTypeExists).WithMessage("Invalid loan type");
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
