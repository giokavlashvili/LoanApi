using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class DeleteApplicationCommandValidator : AbstractValidator<DeleteApplicationCommand>
    {
        private readonly IApplicationDbContext _context;
        private readonly IStringLocalizer _stringLocalizer;

        public DeleteApplicationCommandValidator(IApplicationDbContext context, IStringLocalizer stringLocalizer)
        {
            _context = context;
            _stringLocalizer = stringLocalizer;

            RuleFor(a => a.Id).NotEmpty()
                .MustAsync(LoanApplicationExistsAsync).WithMessage(_stringLocalizer.GetString("InvalidApplication"));
        }

        private Task<bool> LoanApplicationExistsAsync(int loanId, CancellationToken cancellationToken) =>
            _context.LoanApplications.AnyAsync(a => a.Id == loanId, cancellationToken);
    }
}
