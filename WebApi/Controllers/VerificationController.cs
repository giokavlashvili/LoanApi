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
    public class VerificationController : ApiControllerBase
    {
        /// <summary>
        /// Validates the payload, stores it, and sends a code. Every rejection happens here: the
        /// payload is frozen once stored, so a failure at confirm costs a message and a code and
        /// still needs a fresh challenge.
        /// </summary>
        [HttpPost]
        [Route(nameof(Initiate))]
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
        public async Task<ActionResult<OperationResultDto>> Confirm(ConfirmOperationCommand command) =>
            await Mediator.Send(command);

        /// <summary>
        /// Re-sends a code for an operation still awaiting confirmation. Returns a new
        /// <c>challengeId</c>; the <c>operationId</c> is unchanged.
        /// </summary>
        [HttpPost]
        [Route(nameof(Resend))]
        public async Task<ActionResult<PendingOperationDto>> Resend(ResendOperationCodeCommand command) =>
            await Mediator.Send(command);
    }
}
