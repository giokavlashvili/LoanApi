using Application.Operations.Commands;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.Operations.Validators
{
    public class InitiateOperationCommandValidator : AbstractValidator<InitiateOperationCommand>
    {
        public InitiateOperationCommandValidator(IStringLocalizer stringLocalizer)
        {
            RuleFor(c => c.OperationType)
                .NotEmpty().WithMessage(stringLocalizer.GetString("UnknownVerifiableOperation"))
                .MaximumLength(128).WithMessage(stringLocalizer.GetString("UnknownVerifiableOperation"));

            RuleFor(c => c.Channel)
                .IsInEnum().WithMessage(stringLocalizer.GetString("VerificationChannelNotAllowed"));
        }
    }

    public class ConfirmOperationCommandValidator : AbstractValidator<ConfirmOperationCommand>
    {
        public ConfirmOperationCommandValidator(IStringLocalizer stringLocalizer)
        {
            RuleFor(c => c.OperationId)
                .NotNull().WithMessage(stringLocalizer.GetString("PendingOperationNotFound"))
                .NotEqual(Guid.Empty).WithMessage(stringLocalizer.GetString("PendingOperationNotFound"));

            // Only that something was sent. The code itself is checked by the challenge, which
            // counts the attempt -- rejecting a wrong-looking code here would let a caller probe
            // for free, without spending one.
            RuleFor(c => c.Code)
                .NotEmpty().WithMessage(stringLocalizer.GetString("InvalidOtpCode"));
        }
    }

    public class ResendOperationCodeCommandValidator : AbstractValidator<ResendOperationCodeCommand>
    {
        public ResendOperationCodeCommandValidator(IStringLocalizer stringLocalizer)
        {
            RuleFor(c => c.OperationId)
                .NotNull().WithMessage(stringLocalizer.GetString("PendingOperationNotFound"))
                .NotEqual(Guid.Empty).WithMessage(stringLocalizer.GetString("PendingOperationNotFound"));
        }
    }
}
