using Application.Authenticate.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.Authenticate.Validators
{
    public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
    {
        public LogoutCommandValidator(IStringLocalizer stringLocalizer)
        {
            // Only that something was sent. Whether it matches a stored session is deliberately not
            // validated here — the handler is silent about that, and a validator that was not would
            // undo it.
            RuleFor(v => v.RefreshToken)
                .NotEmpty().WithMessage(stringLocalizer.GetString("InvalidRefreshToken"))
                .MaximumLength(512).WithMessage(stringLocalizer.GetString("InvalidRefreshToken"));
        }
    }
}
