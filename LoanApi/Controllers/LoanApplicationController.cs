using Application.LoanApplications.Commands;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class LoanApplicationController : ApiControllerBase
    {
        [HttpPost]
        [Route(nameof(CreateApplication))]
        public async Task<ActionResult<int>> CreateApplication(CreateApplicationCommand command) => await Mediator.Send(command);
    }
}
