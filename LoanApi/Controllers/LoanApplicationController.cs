using Application.Common.Models;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Dtos;
using Application.LoanApplications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Controllers
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
    }
}
