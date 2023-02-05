using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Repositories;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
