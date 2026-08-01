using Application.Common.Interfaces;
using Application.Operations.Dtos;
using Domain.Enums;
using MediatR;
using System.Text.Json;

namespace Application.Operations.Commands
{
    /// <summary>
    /// Captures a request and sends the code that will release it.
    /// <para>
    /// <see cref="ISkipTransaction"/> because <c>IOtpService.IssueAsync</c> persists the challenge
    /// and then sends the message, relying on that save having committed. Reached from inside this
    /// handler the save would only flush, so a commit failure afterwards rolls the challenge back
    /// with the message already delivered — the same reason <c>ResendOtpCommand</c> opts out.
    /// </para>
    /// <para>
    /// The cost is that this performs two saves with no transaction between them. If the second
    /// fails, an orphan challenge is left and no operation id is returned. It self-heals: the
    /// pending-uniqueness index means the caller's next initiate invalidates that challenge and
    /// issues a fresh one.
    /// </para>
    /// </summary>
    public class InitiateOperationCommand : IRequest<PendingOperationDto>, ISkipTransaction
    {
        /// <summary>Registry key. Anything unregistered is refused before a message is sent.</summary>
        public string? OperationType { get; set; }

        public VerificationChannel Channel { get; set; } = VerificationChannel.Sms;

        /// <summary>The operation's own request body, bound to the registry's type — never a type named here.</summary>
        public JsonElement Payload { get; set; }

        /// <summary>
        /// Only honoured by operations declaring <c>AllowsCallerSuppliedRecipient</c>; supplying
        /// one elsewhere is rejected rather than ignored. Everywhere else the code goes to the
        /// number on the authenticated account, which is what stops a caller redirecting their own
        /// confirmation to a phone they control.
        /// </summary>
        public string? Recipient { get; set; }
    }

    public class InitiateOperationCommandHandler : IRequestHandler<InitiateOperationCommand, PendingOperationDto>
    {
        private readonly IVerifiableOperationService _operations;

        public InitiateOperationCommandHandler(IVerifiableOperationService operations)
        {
            _operations = operations;
        }

        public async Task<PendingOperationDto> Handle(InitiateOperationCommand request, CancellationToken cancellationToken) =>
            await _operations.InitiateAsync(
                request.OperationType!, request.Channel, request.Payload, request.Recipient, cancellationToken);
    }
}
