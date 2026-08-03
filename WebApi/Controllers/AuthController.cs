using Application.Authenticate.Commands;
using Application.Authenticate.Dtos;
using Application.Otp.Commands;
using Application.Otp.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Models;

namespace WebApi.Controllers
{
    // Resolves to api/v1/auth. Deliberately not a resource collection: nothing here is CRUD on a
    // thing, so the actions keep explicit verb segments (login, refresh) rather than being bent
    // into a "sessions" resource that no part of the system actually models.
    //
    // The verb comes from the [action] token, kebab-cased by the same transformer that handles
    // [controller] -- so ResendOtp serves resend-otp, and casing is decided in exactly one place.
    // The trade is that renaming a method here silently moves its public URL. That makes any
    // rename in this class an API change; treat it as one.
    [AllowAnonymous]
    [ApiController]
    [Route("api/v1/[controller]")]
    // Every action here has a validator, so every one can 400. There is deliberately no
    // controller-level 401: this controller is anonymous, and the 401s below are credential
    // rejections on two specific actions rather than the missing-token 401 [Authorize] produces.
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public class AuthController : ApiControllerBase
    {
        /// <summary>
        /// 401 covers both an unknown user name and a wrong password — <c>InvalidCredentialsException</c>
        /// does not distinguish them, so that the endpoint cannot be used to enumerate accounts.
        /// </summary>
        [HttpPost("[action]")]
        [ProducesResponseType(typeof(TokenPairDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenPairDto>> Login(LoginCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Two calls. The first omits <c>challengeId</c>/<c>otpCode</c> and answers 428 with the
        /// challenge; the second carries them and creates the account. Nothing is persisted until
        /// the second, so an unconfirmed number never leaves a half-made user behind.
        /// </summary>
        [HttpPost("[action]")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(OtpChallengeProblemDetails), StatusCodes.Status428PreconditionRequired)]
        public async Task<ActionResult<bool>> Register(RegisterUserCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Exchanges a refresh token for a new access token and a new refresh token, invalidating
        /// the one supplied. Anonymous by design: the caller's access token has expired, which is
        /// the only reason to call this.
        /// </summary>
        /// <remarks>
        /// 401, not 400, for a token that is unknown, already spent, revoked or expired: all four
        /// raise <c>InvalidCredentialsException</c> with one message, so a caller cannot learn which
        /// it was. The 400 is only the shape check — missing, or longer than 512 characters.
        /// </remarks>
        [HttpPost("[action]")]
        [ProducesResponseType(typeof(TokenPairDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenPairDto>> Refresh(RefreshTokenCommand command) => await Mediator.Send(command);

        /// <summary>
        /// Revokes the session the refresh token belongs to. Returns 200 whether or not the token
        /// was recognised, so the endpoint cannot be used to probe for valid tokens. Access tokens
        /// already issued stay valid until they expire.
        /// </summary>
        [HttpPost("[action]")]
        // Typeless 200: the action returns Ok() with no body. Without this NSwag falls back to
        // FileResponse in the generated client -- see docs/architecture.md.
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> Logout(LogoutCommand command)
        {
            await Mediator.Send(command);

            return Ok();
        }

        /// <summary>
        /// Re-sends the code for a challenge already issued. Not restricted to registration —
        /// any operation gated by <c>IRequireOtpVerification</c> resends through here.
        /// </summary>
        // 400 also covers an unknown challenge and a throttled resend (OtpChallengeNotFound,
        // OtpThrottled) -- both are DomainValidationException, which the filter maps to 400.
        [HttpPost("[action]")]
        [ProducesResponseType(typeof(OtpChallengeDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<OtpChallengeDto>> ResendOtp(ResendOtpCommand command) => await Mediator.Send(command);
    }
}
