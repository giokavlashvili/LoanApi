using Application.Authenticate.Commands;
using Application.Authenticate.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace LoanApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthenticateController : ApiControllerBase
    {
        [HttpPost]
        [Route(nameof(Login))]
        public async Task<ActionResult<LoginDto>> Login(LoginCommand command) => await Mediator.Send(command);

        [HttpPost]
        [Route(nameof(RegisterUser))]
        public async Task<ActionResult<bool>> RegisterUser(RegisterUserCommand command) => await Mediator.Send(command);
    }
}
