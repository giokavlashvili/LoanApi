using Application.Common.Models;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Dtos;
using Application.LoanApplications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    public class LoanApplicationController : ApiControllerBase
    {
        [HttpGet]
        [Route(nameof(GetApplications))]
        public async Task<ActionResult<PaginatedList<LoanApplicationDto>>> GetApplications([FromQuery]GetApplicationsQuery query) => await Mediator.Send(query);

        [HttpPost]
        [Route(nameof(CreateApplication))]
        public async Task<ActionResult<int>> CreateApplication(CreateApplicationCommand command) => await Mediator.Send(command);

        [HttpPatch]
        [Route(nameof(UpdateApplication))]
        public async Task<ActionResult> UpdateApplication(UpdateApplicationCommand command)
        {
            await Mediator.Send(command);
            return Ok();
        }

        [HttpPatch]
        [Route(nameof(UpdateApplicationStatus))]
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
