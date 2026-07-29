using Application.Authenticate.Commands;
using Application.Authenticate.Dtos;
using Application.Otp.Commands;
using Application.Otp.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
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

        /// <summary>
        /// Re-sends the code for a challenge already issued. Not restricted to registration —
        /// any operation gated by <c>IRequireOtpVerification</c> resends through here.
        /// </summary>
        [HttpPost]
        [Route(nameof(ResendOtp))]
        public async Task<ActionResult<OtpChallengeDto>> ResendOtp(ResendOtpCommand command) => await Mediator.Send(command);
    }
}
