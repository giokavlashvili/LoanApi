using Application.Authenticate.Commands;
using Application.Authenticate.Dtos;
using Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthenticateController : ApiControllerBase
    {
        private readonly ICurrentUserService _currentUserService;

        public AuthenticateController(ICurrentUserService currentUserService)
        {
            _currentUserService = currentUserService;
        }

        [HttpGet]
        [Route(nameof(Check))]
        [Authorize]
        public ActionResult<AuthCheckDto> Check()
        {
            // If we reach here, user is authenticated (Authorize attribute ensures this)
            return Ok(new AuthCheckDto
            {
                IsAuthenticated = true,
                UserId = _currentUserService.UserId,
                UserName = _currentUserService.Name,
                Email = _currentUserService.Email
            });
        }

        [HttpPost]
        [Route(nameof(Login))]
        public async Task<ActionResult<LoginDto>> Login(LoginCommand command) => await Mediator.Send(command);

        [HttpPost]
        [Route(nameof(RegisterUser))]
        public async Task<ActionResult<bool>> RegisterUser(RegisterUserCommand command) => await Mediator.Send(command);
    }
}
