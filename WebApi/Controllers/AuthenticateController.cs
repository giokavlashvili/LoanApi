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
        public async Task<ActionResult<TokenPairDto>> Login(LoginCommand command) => await Mediator.Send(command);

        [HttpPost]
        [Route(nameof(RegisterUser))]
        public async Task<ActionResult<bool>> RegisterUser(RegisterUserCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Exchanges a refresh token for a new access token and a new refresh token, invalidating
        /// the one supplied. Anonymous by design: the caller's access token has expired, which is
        /// the only reason to call this.
        /// </summary>
        [HttpPost]
        [Route(nameof(Refresh))]
        public async Task<ActionResult<TokenPairDto>> Refresh(RefreshTokenCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Revokes the session the refresh token belongs to. Returns 200 whether or not the token
        /// was recognised, so the endpoint cannot be used to probe for valid tokens. Access tokens
        /// already issued stay valid until they expire.
        /// </summary>
        [HttpPost]
        [Route(nameof(Logout))]
        public async Task<ActionResult> Logout(LogoutCommand command)
        {
            await Mediator.Send(command);

            return Ok();
        }

        /// <summary>
        /// Re-sends the code for a challenge already issued. Not restricted to registration —
        /// any operation gated by <c>IRequireOtpVerification</c> resends through here.
        /// </summary>
        [HttpPost]
        [Route(nameof(ResendOtp))]
        public async Task<ActionResult<OtpChallengeDto>> ResendOtp(ResendOtpCommand command) => await Mediator.Send(command);
    }
}
