using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class UpdateApplicationStatusCommandValidator : AbstractValidator<UpdateApplicationStatusCommand>
    {
        private readonly IApplicationDbContext _context;
        private readonly IStringLocalizer _stringLocalizer;

        public UpdateApplicationStatusCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;

            // Synchronous, deliberately — see CreateApplicationCommandValidator.
            RuleFor(a => a.Id).NotEmpty()
                .Must(LoanApplicationExists).WithMessage(_stringLocalizer.GetString("InvalidApplication"));
        }

        public bool LoanApplicationExists(int loanId)
        {
            return _context.LoanApplications.Any(a => a.Id == loanId);
        }
    }
}
