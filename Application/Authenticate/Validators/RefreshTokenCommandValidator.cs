using Application.Authenticate.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.Authenticate.Validators
{
    public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
    {
        public RefreshTokenCommandValidator(IStringLocalizer stringLocalizer)
        {
            // The same key the rotation failures use. A missing token and an unrecognised one are
            // the same thing from the caller's side, and reporting them differently would be one
            // more way to tell whether a value was a real token.
            RuleFor(v => v.RefreshToken)
                .NotEmpty().WithMessage(stringLocalizer.GetString("InvalidRefreshToken"))
                .MaximumLength(512).WithMessage(stringLocalizer.GetString("InvalidRefreshToken"));
        }
    }
}
