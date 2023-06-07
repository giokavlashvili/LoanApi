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
    [Route("api/v{version:apiVersion}/[controller]")]
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

        [HttpDelete]
        [Route(nameof(DeleteApplication))]
        public async Task<ActionResult> DeleteApplication(DeleteApplicationCommand command)
        {
            await Mediator.Send(command);
            return Ok();
        }
    }
}
