using Application.Currencies.Dtos;
using Application.Currencies.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [Authorize]
    [ApiController]
    // Resolves to api/v1/currencies -- the [controller] token is lowercased and kebab-cased by
    // SlugifyParameterTransformer, so the plural class name is the whole route definition.
    [Route("api/v1/[controller]")]
    // Mirrors [Authorize] above: every action here is gated, so every action can 401.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public class CurrenciesController : ApiControllerBase
    {
        // No 400: the query takes no input and has no validator, so there is nothing to reject.
        [HttpGet]
        [ProducesResponseType(typeof(List<CurrencyDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<CurrencyDto>>> GetAll() => await Mediator.Send(new GetCurrenciesQuery());
    }
}
