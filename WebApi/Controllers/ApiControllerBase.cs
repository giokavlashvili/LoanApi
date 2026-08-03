using MediatR;

using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    // No [Route] here on purpose. MVC's CreateControllerModel walks up the hierarchy and stops at
    // the first type carrying a route attribute, so a template declared here would be silently
    // discarded by every derived controller that declares its own -- which all of them do. It used
    // to say "api/[controller]", an unversioned template that never served a request and read as
    // though it did. Each controller owns its route; there is exactly one to look at.
    //
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