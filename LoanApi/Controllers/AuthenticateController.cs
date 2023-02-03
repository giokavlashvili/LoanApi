using Application.Authenticate.Commands;
using Application.Authenticate.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthenticateController : ApiControllerBase
    {
        [HttpPost]
        [Route(nameof(Login))]
        public async Task<ActionResult<LoginDto>> Login(LoginCommand query)
        {
            return await Mediator.Send(query);
        }

        [HttpPost]
        [Route(nameof(RegisterUser))]
        public async Task<ActionResult<bool>> RegisterUser(RegisterUserCommand query)
        {
            return await Mediator.Send(query);
        }
    }
}
