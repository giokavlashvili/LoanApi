using Application.Operations.Commands;
using Application.Operations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    /// <summary>
    /// The generic two-step flow: <c>Initiate</c> captures a request and sends a code,
    /// <c>Confirm</c> answers it and runs the operation. An operation opts in by carrying
    /// <c>[VerifiableOperation]</c>; nothing here knows what any of them do.
    /// <para>
    /// <strong>The request body is stored, unencrypted, between the two calls.</strong> Database
    /// access is controlled at the infrastructure level and that is the accepted trade — but an
    /// operation carrying genuinely sensitive data should use <c>IRequireOtpVerification</c>
    /// instead, which never persists a payload.
    /// </para>
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    // Controller-wide because all three actions share them. The 400 is broader here than
    // elsewhere: besides the command validators it carries every DomainValidationException the
    // flow raises -- PendingOperationNotFound, PendingOperationAlreadyCompleted,
    // PendingOperationUnavailable, OtpRecipientNotAllowed, UnknownVerifiableOperation, and the
    // whole Otp* set (InvalidOtpCode, OtpExpired, OtpLocked, OtpAlreadyUsed, OtpThrottled,
    // OtpPurposeMismatch, OtpRequestMismatch).
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public class VerificationController : ApiControllerBase
    {
        /// <summary>
        /// Validates the payload, stores it, and sends a code. Every rejection happens here: the
        /// payload is frozen once stored, so a failure at confirm costs a message and a code and
        /// still needs a fresh challenge.
        /// </summary>
        [HttpPost]
        [Route(nameof(Initiate))]
        [ProducesResponseType(typeof(PendingOperationDto), StatusCodes.Status200OK)]
        // The operation's own RequiredPolicies, checked before a code is sent.
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PendingOperationDto>> Initiate(InitiateOperationCommand command) =>
            await Mediator.Send(command);

        /// <summary>
        /// Verifies the code and runs the operation. Safe to retry with the same code after a lost
        /// response — the stored result is replayed rather than the operation re-run.
        /// <para>
        /// <c>result</c> is untyped by construction, since one endpoint returns every operation's
        /// result. A client wanting a typed result should ignore it and re-fetch the resource.
        /// </para>
        /// </summary>
        [HttpPost]
        [Route(nameof(Confirm))]
        [ProducesResponseType(typeof(OperationResultDto), StatusCodes.Status200OK)]
        // Re-checked here, not only at initiate: minutes pass in between, which is long enough for
        // a role to be revoked.
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        // Two concurrent confirms of the same operation. OtpVerification carries a RowVersion, so
        // the loser's SaveChanges throws DbUpdateConcurrencyException rather than running twice --
        // which is why there is no Executing status to manage.
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<OperationResultDto>> Confirm(ConfirmOperationCommand command) =>
            await Mediator.Send(command);

        /// <summary>
        /// Re-sends a code for an operation still awaiting confirmation. Returns a new
        /// <c>challengeId</c>; the <c>operationId</c> is unchanged.
        /// </summary>
        // No 403: unlike the two above, resending does not re-run the operation's policy checks --
        // it only reissues a code for an operation the caller already owns.
        [HttpPost]
        [Route(nameof(Resend))]
        [ProducesResponseType(typeof(PendingOperationDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<PendingOperationDto>> Resend(ResendOperationCodeCommand command) =>
            await Mediator.Send(command);
    }
}
