using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
        // I/O rules use MustAsync so ValidationBehavior's ValidateAsync path can await EF
        // without blocking. Pure format rules elsewhere may stay synchronous.
        public CreateApplicationCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;
            RuleFor(a => a.CurrencyId).MustAsync(CurrencyExistsAsync).WithMessage(_stringLocalizer.GetString("InvalidCurrency"));

            RuleFor(a => a.LoanTypeId).MustAsync(LoanTypeExistsAsync).WithMessage(_stringLocalizer.GetString("InvalidLoanType"));
        }

        private Task<bool> CurrencyExistsAsync(int currencyId, CancellationToken cancellationToken) =>
            _context.Currencies.AnyAsync(c => c.Id == currencyId, cancellationToken);

        private Task<bool> LoanTypeExistsAsync(int loanTypeId, CancellationToken cancellationToken) =>
            _context.LoanTypes.AnyAsync(t => t.Id == loanTypeId, cancellationToken);
    }
}
