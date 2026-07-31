using Application.Confirmation.Commands;
using FluentValidation;

namespace Application.Confirmation.Validators
{
    public class ConfirmOperationCommandValidator : AbstractValidator<ConfirmOperationCommand>
    {
        public ConfirmOperationCommandValidator()
        {
            RuleFor(c => c.OperationId)
                .NotEmpty().WithMessage("PendingOperationNotFound");

            RuleFor(c => c.ChallengeId)
                .NotEmpty().WithMessage("OtpChallengeNotFound");

            RuleFor(c => c.OtpCode)
                .NotEmpty().WithMessage("InvalidOtpCode");
        }
    }
}
