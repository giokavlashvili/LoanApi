using Application.Authenticate.Dtos;
using Application.Common.Interfaces;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.Authenticate.Commands
{
    public class LoginCommand : IRequest<TokenPairDto>
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
    }

    public class LoginCommandHandler : IRequestHandler<LoginCommand, TokenPairDto>
    {
        private readonly IIdentityService _identityService;

        public LoginCommandHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<TokenPairDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var result = await _identityService.AuthenticateAsync(request.UserName, request.Password, cancellationToken);

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
