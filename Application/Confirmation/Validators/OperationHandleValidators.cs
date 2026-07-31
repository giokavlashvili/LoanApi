using Application.Confirmation.Commands;
using FluentValidation;

namespace Application.Confirmation.Validators
{
    public class RequestOperationChallengeCommandValidator : AbstractValidator<RequestOperationChallengeCommand>
    {
        public RequestOperationChallengeCommandValidator()
        {
            RuleFor(c => c.OperationId)
                .NotEmpty().WithMessage("PendingOperationNotFound");
        }
    }

    public class CancelOperationCommandValidator : AbstractValidator<CancelOperationCommand>
    {
        public CancelOperationCommandValidator()
        {
            RuleFor(c => c.OperationId)
                .NotEmpty().WithMessage("PendingOperationNotFound");
        }
    }
}
