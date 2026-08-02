using Application.Common.Models;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Dtos;
using Application.LoanApplications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Models;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    // Controller-wide: every action has a validator, and every action is gated by [Authorize].
    // The 400 covers both the validators (InvalidCurrency, InvalidLoanType, InvalidApplication,
    // InvalidPageNumber, InvalidPageSize) and the aggregate's own guards, which the filter maps
    // the same way (InvalidAmount, InvalidPeriod, ApplicationAlreadyProcessed).
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public class LoanApplicationController : ApiControllerBase
    {
        [HttpGet]
        [Route(nameof(GetApplications))]
        [ProducesResponseType(typeof(PaginatedList<LoanApplicationDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<PaginatedList<LoanApplicationDto>>> GetApplications([FromQuery]GetApplicationsQuery query) => await Mediator.Send(query);

        /// <summary>Returns the new application's id.</summary>
        // No 409: a concurrency conflict needs an existing row to contend for, and this inserts one.
        [HttpPost]
        [Route(nameof(CreateApplication))]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        public async Task<ActionResult<int>> CreateApplication(CreateApplicationCommand command) => await Mediator.Send(command);

        [HttpPatch]
        [Route(nameof(UpdateApplication))]
        // Typeless 200: the action returns Ok() with no body. Without this NSwag falls back to
        // FileResponse in the generated client -- see docs/architecture.md.
        [ProducesResponseType(StatusCodes.Status200OK)]
        // LoanApplication carries a RowVersion, so a second concurrent writer to the same row gets
        // DbUpdateConcurrencyException rather than silently overwriting.
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult> UpdateApplication(UpdateApplicationCommand command)
        {
            await Mediator.Send(command);
            return Ok();
        }

        /// <summary>
        /// Gated by <c>IRequireOtpVerification</c>, so it takes two calls: the first omits
        /// <c>challengeId</c>/<c>otpCode</c> and answers 428 with the challenge, the second carries
        /// them and applies the change. The code goes to the number on the authenticated account —
        /// unlike registration, the request cannot name a recipient.
        /// </summary>
        [HttpPatch]
        [Route(nameof(UpdateApplicationStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(OtpChallengeProblemDetails), StatusCodes.Status428PreconditionRequired)]
        public async Task<ActionResult> UpdateApplicationStatus(UpdateApplicationStatusCommand command)
        {
            await Mediator.Send(command);
            return Ok();
        }

        // DeleteApplication has no direct route on purpose. It carries
        // [VerifiableOperation("DeleteLoanApplication")], so it is reached through
        // Verification/Initiate then Verification/Confirm.
        //
        // Registering an operation does not gate its existing endpoint. Leaving a direct
        // DELETE here would have left the confirmation entirely optional — a caller who
        // skipped it would delete just the same, and the gate would be decoration.
    }
}
