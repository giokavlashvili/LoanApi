using Application.LoanTypes.Dtos;
using Application.LoanTypes.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class LoanTypeController : ApiControllerBase
    {
        [HttpGet]
        [Route(nameof(GetLoanTypes))]
        public async Task<ActionResult<List<LoanTypeDto>>> GetLoanTypes() => await Mediator.Send(new GetLoanTypesQuery());
    }
}
