using Application.Authenticate.Dtos;
using Application.Common.Interfaces;
using Application.Common.Logging;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.Authenticate.Commands
{
    /// <summary>
    /// Trades a refresh token for a new pair. Deliberately not gated by <c>[Authorize]</c> at the
    /// controller: the caller's access token is expired by definition, which is why it is here.
    /// </summary>
    public class RefreshTokenCommand : IRequest<TokenPairDto>
    {
        [SensitiveData]
        public string? RefreshToken { get; set; }
    }

    public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, TokenPairDto>
    {
        private readonly IIdentityService _identityService;

        public RefreshTokenCommandHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<TokenPairDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
        {
            var result = await _identityService.RefreshAsync(request.RefreshToken, cancellationToken);

            return new TokenPairDto()
            {
                AccessToken = result.AccessToken,
                ValidTo = result.ValidTo,
                RefreshToken = result.RefreshToken,
                RefreshTokenValidTo = result.RefreshTokenValidTo
            };
        }
    }
}
