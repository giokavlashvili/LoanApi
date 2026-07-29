using Application.Otp.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.Otp.Validators
{
    public class ResendOtpCommandValidator : AbstractValidator<ResendOtpCommand>
    {
        private readonly IStringLocalizer _stringLocalizer;

        public ResendOtpCommandValidator(IStringLocalizer stringLocalizer)
        {
            _stringLocalizer = stringLocalizer;

            RuleFor(c => c.ChallengeId)
                .NotNull().WithMessage(_stringLocalizer.GetString("OtpChallengeNotFound"))
                .NotEqual(Guid.Empty).WithMessage(_stringLocalizer.GetString("OtpChallengeNotFound"));
        }
    }
}
