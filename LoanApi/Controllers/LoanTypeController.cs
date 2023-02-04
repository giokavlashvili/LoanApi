using Application.Currencies.Dtos;
using Application.Currencies.Queries;
using Application.LoanTypes.Dtos;
using Application.LoanTypes.Queries;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class LoanTypeController : ApiControllerBase
    {
        [HttpGet]
        [Route(nameof(GetLoanTypes))]
        public async Task<ActionResult<List<LoanTypeDto>>> GetLoanTypes() => await Mediator.Send(new GetLoanTypesQuery());
    }
}
