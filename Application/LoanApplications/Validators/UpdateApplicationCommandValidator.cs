using Application.LoanApplications.Commands;
using Domain.Repositories;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IStringLocalizer _stringLocalizer;

        public UpdateApplicationCommandValidator(IUnitOfWork unitOfWork, IStringLocalizer stringLocalizer)
        {
            _unitOfWork = unitOfWork;
            _stringLocalizer = stringLocalizer;

            RuleFor(a => a.Id)
                .Custom((id, context) =>
                {
                    var entity = _unitOfWork.LoanApplicationRepository
                        .Get(a => a.Id == id, includeProperties: "Currency,LoanType")
                        .FirstOrDefault();

                    if (entity == null)
                        context.AddFailure(_stringLocalizer.GetString("InvalidApplication"));
                    else if (entity.LoanType == null)
                        context.AddFailure(_stringLocalizer.GetString("InvalidLoanType"));
                    else if (entity.Currency == null)
                        context.AddFailure(_stringLocalizer.GetString("InvalidCurrency"));

                });
        }
    }
}