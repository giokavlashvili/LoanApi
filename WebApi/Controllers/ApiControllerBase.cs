using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // The one response every action in the API can produce: UnhandledExceptionHandlerMiddlware
    // turns anything the exception filter does not map into a ProblemDetails 500. Declared here
    // rather than repeated 15 times because it is genuinely universal — and nothing else is.
    // 400 in particular is not: GetCurrencies and GetLoanTypes take no input and have no validator,
    // so claiming it on the base would document a response they cannot return.
    //
    // Type-level attributes are Inherited, and MVC builds its ControllerModel with
    // GetCustomAttributes(inherit: true), so this reaches every derived controller's actions.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public abstract class ApiControllerBase : ControllerBase
    {
        private ISender _mediator = null!;

        protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
    }
}