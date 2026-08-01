using Application.Operations.Dtos;
using Domain.Enums;
using System.Text.Json;

namespace Application.Common.Interfaces
{
    /// <summary>
    /// The generic two-step flow: capture a request, gate it on a code, then run it. Independent
    /// of <c>IRequireOtpVerification</c> — a command opts in here by carrying
    /// <c>[VerifiableOperation]</c> instead, and must not do both.
    /// </summary>
    public interface IVerifiableOperationService
    {
        /// <summary>
        /// Validates the payload, stores it, issues a challenge and sends the code. Every
        /// rejection happens here rather than at confirm: the payload is frozen once stored, so a
        /// failure later costs a message <em>and</em> a code and still needs a fresh challenge.
        /// </summary>
        Task<PendingOperationDto> InitiateAsync(
            string operationType,
            VerificationChannel channel,
            JsonElement payload,
            string? recipient = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies the code and runs the operation, or replays the stored result if it has
        /// already run. Code verification happens outside the transaction that wraps execution —
        /// see the implementation for why that ordering is load-bearing.
        /// </summary>
        Task<OperationResultDto> ConfirmAsync(Guid operationId, string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Issues a fresh code for an operation still awaiting confirmation, and moves the
        /// operation onto the new challenge — the old one is retired, so without that move every
        /// later confirm would verify against a dead challenge.
        /// </summary>
        Task<PendingOperationDto> ResendAsync(Guid operationId, CancellationToken cancellationToken = default);
    }
}
