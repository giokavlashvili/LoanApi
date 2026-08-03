using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    /// <summary>
    /// Also the demonstration that <strong>validation sees plaintext</strong>. The length and digit
    /// rules below would pass unconditionally against ciphertext — every protected value is a long
    /// base64 string — so if they ever start passing for a two-digit card number, encryption has
    /// been moved above validation in <c>VerifiableOperationService.InitiateAsync</c> and the
    /// ordering there needs putting back.
    /// </summary>
    public class SubmitPayoutDetailsCommandValidator : AbstractValidator<SubmitPayoutDetailsCommand>
    {
        private readonly IApplicationDbContext _context;
        private readonly IStringLocalizer _stringLocalizer;

        public SubmitPayoutDetailsCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;

            RuleFor(p => p.ApplicationId).NotEmpty()
                .MustAsync(LoanApplicationExistsAsync).WithMessage(_stringLocalizer.GetString("InvalidApplication"));

            RuleFor(p => p.CardNumber)
                .NotEmpty().WithMessage(_stringLocalizer.GetString("InvalidCardNumber"))
                .Matches("^[0-9]{12,19}$").WithMessage(_stringLocalizer.GetString("InvalidCardNumber"));

            RuleFor(p => p.PersonalNumber)
                .NotEmpty().WithMessage(_stringLocalizer.GetString("InvalidPersonalNumber"))
                .Matches("^[0-9]{11}$").WithMessage(_stringLocalizer.GetString("InvalidPersonalNumber"));

            RuleFor(p => p.BankName)
                .MaximumLength(128).WithMessage(_stringLocalizer.GetString("InvalidBankName"));
        }

        private Task<bool> LoanApplicationExistsAsync(int loanId, CancellationToken cancellationToken) =>
            _context.LoanApplications.AnyAsync(a => a.Id == loanId, cancellationToken);
    }
}
