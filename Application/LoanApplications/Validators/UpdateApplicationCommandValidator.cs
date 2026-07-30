using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationCommandValidator : AbstractValidator<UpdateApplicationCommand>
    {
        private readonly IApplicationDbContext _context;
        private readonly IStringLocalizer _stringLocalizer;

        public UpdateApplicationCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;

            // Synchronous, deliberately — see CreateApplicationCommandValidator: MVC's automatic
            // validation runs this validator too, and it cannot invoke an async rule.
            RuleFor(a => a.Id)
                .Custom((id, validationContext) =>
                {
                    // Two booleans instead of the whole aggregate plus both navigations: the rule
                    // only needs to know whether the row and its reference data exist. Replaces a
                    // Get(filter, includeProperties: "Currency,LoanType") call whose include list
                    // was a magic string no rename would ever have followed.
                    var found = _context.LoanApplications
                        .AsNoTracking()
                        .Where(a => a.Id == id)
                        .Select(a => new
                        {
                            HasLoanType = a.LoanType != null,
                            HasCurrency = a.Currency != null
                        })
                        .FirstOrDefault();

                    if (found == null)
                        validationContext.AddFailure(_stringLocalizer.GetString("InvalidApplication"));
                    else if (!found.HasLoanType)
                        validationContext.AddFailure(_stringLocalizer.GetString("InvalidLoanType"));
                    else if (!found.HasCurrency)
                        validationContext.AddFailure(_stringLocalizer.GetString("InvalidCurrency"));

                });
        }
    }
}
