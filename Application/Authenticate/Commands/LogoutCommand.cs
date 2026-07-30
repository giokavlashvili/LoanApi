using Application.Common.Interfaces;
using Application.Common.Logging;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.Authenticate.Commands
{
    /// <summary>
    /// Ends the session the supplied refresh token belongs to, revoking the whole rotation lineage.
    /// <para>
    /// Succeeds whether or not the token was real. Logout is not an authorization decision, and a
    /// failure response would confirm which tokens exist. Note it cannot invalidate an access token
    /// already issued — those remain valid until they expire.
    /// </para>
    /// </summary>
    public class LogoutCommand : IRequest
    {
        [SensitiveData]
        public string? RefreshToken { get; set; }
    }

    public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
    {
        private readonly IRefreshTokenService _refreshTokenService;

        public LogoutCommandHandler(IRefreshTokenService refreshTokenService)
        {
            _refreshTokenService = refreshTokenService;
        }

        public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
        {
            await _refreshTokenService.RevokeSessionAsync(request.RefreshToken, cancellationToken);
        }
    }
}
