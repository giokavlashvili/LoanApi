using Application.Common.Interfaces;
using Application.Common.Logging;
using Application.Operations.Dtos;
using MediatR;

namespace Application.Operations.Commands
{
    /// <summary>
    /// Answers a challenge and runs the operation it was gating.
    /// <para>
    /// <see cref="ISkipTransaction"/> because the automatic boundary starts too early. Code
    /// verification must commit on its own: <c>OtpService.VerifyAsync</c> increments
    /// <c>AttemptCount</c> and saves it in a <c>finally</c> so a wrong code always counts, and
    /// inside a transaction the throw would roll that increment back — <c>MaxAttempts</c> would
    /// never be reached and six digits would be brute-forceable. The service therefore opens its
    /// own narrower transaction around execution and the result write only.
    /// </para>
    /// </summary>
    public class ConfirmOperationCommand : IRequest<OperationResultDto>, ISkipTransaction
    {
        public Guid? OperationId { get; set; }

        [SensitiveData]
        public string? Code { get; set; }
    }

    public class ConfirmOperationCommandHandler : IRequestHandler<ConfirmOperationCommand, OperationResultDto>
    {
        private readonly IVerifiableOperationService _operations;

        public ConfirmOperationCommandHandler(IVerifiableOperationService operations)
        {
            _operations = operations;
        }

        public async Task<OperationResultDto> Handle(ConfirmOperationCommand request, CancellationToken cancellationToken) =>
            await _operations.ConfirmAsync(request.OperationId!.Value, request.Code!, cancellationToken);
    }
}
