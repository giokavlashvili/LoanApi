using Application.Common.Interfaces;
using Application.Operations.Dtos;
using MediatR;

namespace Application.Operations.Commands
{
    /// <summary>
    /// Re-issues a code for an operation still awaiting confirmation. Deliberately not gated
    /// itself — it is the way out of a lost message, so requiring a code to get a code would
    /// deadlock the flow.
    /// <para>
    /// <see cref="ISkipTransaction"/> for the same reason as
    /// <see cref="InitiateOperationCommand"/>: it reaches <c>IssueAsync</c>, which saves the
    /// challenge and then sends the message.
    /// </para>
    /// </summary>
    public class ResendOperationCodeCommand : IRequest<PendingOperationDto>, ISkipTransaction
    {
        public Guid? OperationId { get; set; }
    }

    public class ResendOperationCodeCommandHandler : IRequestHandler<ResendOperationCodeCommand, PendingOperationDto>
    {
        private readonly IVerifiableOperationService _operations;

        public ResendOperationCodeCommandHandler(IVerifiableOperationService operations)
        {
            _operations = operations;
        }

        public async Task<PendingOperationDto> Handle(ResendOperationCodeCommand request, CancellationToken cancellationToken) =>
            await _operations.ResendAsync(request.OperationId!.Value, cancellationToken);
    }
}
