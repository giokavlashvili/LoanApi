using Application.Currencies.Dtos;
using Application.Currencies.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    // Mirrors [Authorize] above: every action here is gated, so every action can 401.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public class CurrencyController : ApiControllerBase
    {
        // No 400: the query takes no input and has no validator, so there is nothing to reject.
        [HttpGet]
        [Route(nameof(GetCurrencies))]
        [ProducesResponseType(typeof(List<CurrencyDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<CurrencyDto>>> GetCurrencies() => await Mediator.Send(new GetCurrenciesQuery());
    }
}
