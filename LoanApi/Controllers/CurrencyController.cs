using Application.Currencies.Dtos;
using Application.Currencies.Queries;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CurrencyController : ApiControllerBase
    {
        [HttpGet]
        [Route(nameof(GetCurrencies))]
        public async Task<ActionResult<List<CurrencyDto>>> GetCurrencies() => await Mediator.Send(new GetCurrenciesQuery());
    }
}
