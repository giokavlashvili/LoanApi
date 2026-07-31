using Application.Confirmation.Commands;
using Application.Confirmation.Dtos;
using Application.Confirmation.Queries;
using Application.Otp.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    /// <summary>
    /// The confirmation half of the initiate/confirm flow. There is no initiate endpoint here —
    /// initiating is just calling the operation's own endpoint, which parks the command and
    /// answers 202 with a handle.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    public class OperationsController : ApiControllerBase
    {
        /// <summary>
        /// Texts a code for this operation to the caller. Needed whenever the confirmer is not the
        /// initiator, since no code is issued at initiate in that case.
        /// </summary>
        [HttpPost]
        [Route("{operationId:guid}/challenge")]
        public async Task<ActionResult<OtpChallengeDto>> RequestChallenge(Guid operationId) =>
            await Mediator.Send(new RequestOperationChallengeCommand { OperationId = operationId });

        /// <summary>
        /// Redeems the operation and runs the command that was captured at initiate.
        /// </summary>
        [HttpPost]
        [Route("{operationId:guid}/confirm")]
        public async Task<ActionResult> Confirm(Guid operationId, ConfirmOperationCommand command)
        {
            // The route is authoritative, so a body disagreeing with the URL cannot confirm a
            // different operation than the one addressed.
            command.OperationId = operationId;

            await Mediator.Send(command);

            return NoContent();
        }

        [HttpPost]
        [Route("{operationId:guid}/cancel")]
        public async Task<ActionResult> Cancel(Guid operationId)
        {
            await Mediator.Send(new CancelOperationCommand { OperationId = operationId });

            return NoContent();
        }

        [HttpGet]
        [Route("pending")]
        public async Task<ActionResult<IReadOnlyList<PendingOperationDto>>> Pending() =>
            Ok(await Mediator.Send(new GetPendingOperationsQuery()));
    }
}
