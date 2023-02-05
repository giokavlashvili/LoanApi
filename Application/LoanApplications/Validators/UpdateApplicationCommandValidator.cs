using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;

        public UpdateApplicationCommandValidator(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;

            RuleFor(a => a.Id).NotEmpty()
                .Custom((id, context) => 
                {
                    var entity = _unitOfWork.LoanApplicationRepository
                        .Get(a => a.Id == id, includeProperties: "Currency,LoanType")
                        .FirstOrDefault();

                    if (entity == null)
                        context.AddFailure("Invalid application");
                    else if(entity.Status == LoanStatus.Accepted || entity.Status == LoanStatus.Rejected)
                        context.AddFailure("Application is already processed");
                    else if(entity.LoanType == null)
                        context.AddFailure("Invalid loan type");
                    else if (entity.Currency == null)
                        context.AddFailure("Invalid currency");

                });

            RuleFor(a => a.PeriodPerMonth).GreaterThan(0).WithMessage("Period should be greater than 0");

            RuleFor(a => a.Amount).GreaterThan(0).WithMessage("Amount should be greater than 0");
        }
    }
}
