using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
    {
        private readonly IApplicationDbContext _context;
        private readonly IStringLocalizer _stringLocalizer;

        // Existence checks are reads, not aggregate loads: Currency and LoanType are reference
        // data with no invariants, so they have no repository.
        //
        // The rules are synchronous, and must stay that way. WebApi registers
        // AddFluentValidationAutoValidation(), so MVC's model-validation pipeline runs every
        // validator in *addition* to ValidationBehavior — and that pipeline is synchronous.
        // A MustAsync/CustomAsync rule anywhere in here makes MVC throw
        // AsyncValidatorInvokedSynchronouslyException and the endpoint 500s before the handler
        // is ever reached. That is also why these take no CancellationToken.
        public CreateApplicationCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;
            RuleFor(a => a.CurrencyId).Must(CurrencyExists).WithMessage(_stringLocalizer.GetString("InvalidCurrency"));

            RuleFor(a => a.LoanTypeId).Must(LoanTypeExists).WithMessage(_stringLocalizer.GetString("InvalidLoanType"));
        }

        public bool CurrencyExists(int currencyId)
        {
            return _context.Currencies.Any(c => c.Id == currencyId);
        }

        public bool LoanTypeExists(int loanTypeId)
        {
            return _context.LoanTypes.Any(t => t.Id == loanTypeId);
        }
    }
}
