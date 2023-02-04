using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanApplications.Validators
{
    public class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;

        public CreateApplicationCommandValidator(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;

            RuleFor(a => a.PeriodPerMonth).GreaterThan(0).WithMessage("Period should be greater than 0");

            //RuleFor(a => a.Amount).GreaterThan(0).WithMessage("Amount should be greater than 0");

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
