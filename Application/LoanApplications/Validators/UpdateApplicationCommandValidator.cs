using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;

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
                    else if(entity.LoanType == null)
                        context.AddFailure("Invalid loan type");
                    else if (entity.Currency == null)
                        context.AddFailure("Invalid currency");

                });
        }
    }
}