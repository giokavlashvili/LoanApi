using Application.Common.Models;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Dtos;
using Application.LoanApplications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Models;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    // Resolves to api/v1/loan-applications. The resource is the collection, so the actions carry
    // no verb of their own -- the HTTP method already says what is happening, and repeating it in
    // the path (the old CreateApplication, GetApplications) said it twice.
    [Route("api/v1/[controller]")]
    // Controller-wide: every action has a validator, and every action is gated by [Authorize].
    // The 400 covers both the validators (InvalidCurrency, InvalidLoanType, InvalidApplication,
    // InvalidPageNumber, InvalidPageSize) and the aggregate's own guards, which the filter maps
    // the same way (InvalidAmount, InvalidPeriod, ApplicationAlreadyProcessed).
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public class LoanApplicationsController : ApiControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(PaginatedList<LoanApplicationDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaginatedList<LoanApplicationDto>>> GetAll([FromQuery] GetApplicationsQuery query) => await Mediator.Send(query);

        /// <summary>Returns the new application's id.</summary>
        // No 409: a concurrency conflict needs an existing row to contend for, and this inserts one.
        //
        // Still 200 rather than 201: a 201 is only worth the name with a Location header, and there
        // is no GET api/v1/loan-applications/{id} for it to point at yet. A Location that 404s is
        // worse than none. Adding that endpoint is the open item -- see docs/architecture.md.
        [HttpPost]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        public async Task<ActionResult<int>> Create(CreateApplicationCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Replaces the application's editable fields. PUT, not PATCH: every field on
        /// <c>UpdateApplicationCommand</c> is required and the handler assigns all of them, so the
        /// call has always been a full replace -- PATCH advertised a partial update the endpoint
        /// does not implement.
        /// </summary>
        [HttpPut("{id:int}")]
        // Typeless 200: the action returns Ok() with no body. Without this NSwag falls back to
        // FileResponse in the generated client -- see docs/architecture.md.
        [ProducesResponseType(StatusCodes.Status200OK)]
        // LoanApplication carries a RowVersion, so a second concurrent writer to the same row gets
        // DbUpdateConcurrencyException rather than silently overwriting.
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult> Update(int id, UpdateApplicationCommand command)
        {
            // The route wins over the body. Both carry an id now that the path addresses the
            // resource, and the pair can disagree -- either by a client bug or deliberately, to
            // aim an authorized-looking URL at a different row. Overwriting before the command
            // reaches the pipeline means the validator and the handler only ever see the one the
            // caller was routed to.
            await Mediator.Send(command with { Id = id });

            return Ok();
        }

        /// <summary>
        /// Gated by <c>IRequireOtpVerification</c>, so it takes two calls: the first omits
        /// <c>challengeId</c>/<c>otpCode</c> and answers 428 with the challenge, the second carries
        /// them and applies the change. The code goes to the number on the authenticated account —
        /// unlike registration, the request cannot name a recipient.
        /// </summary>
        // A sub-resource rather than a field on the PUT above: status is the one part of an
        // application that moves on its own, and the only part behind the OTP gate.
        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(OtpChallengeProblemDetails), StatusCodes.Status428PreconditionRequired)]
        public async Task<ActionResult> UpdateStatus(int id, UpdateApplicationStatusCommand command)
        {
            // Same reasoning as Update. It matters more here: the OTP challenge hashes the request,
            // so the id has to be settled before the gate sees it or the confirm would be checked
            // against a payload the caller could still redirect.
            await Mediator.Send(command with { Id = id });

            return Ok();
        }

        // Delete has no route on purpose. DeleteApplicationCommand carries
        // [VerifiableOperation("DeleteLoanApplication")], so it is reached through
        // Verification/Initiate then Verification/Confirm.
        //
        // Registering an operation does not gate its existing endpoint. Leaving a direct
        // DELETE here would have left the confirmation entirely optional — a caller who
        // skipped it would delete just the same, and the gate would be decoration.
    }
}
