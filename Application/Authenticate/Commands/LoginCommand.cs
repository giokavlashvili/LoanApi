using Application.Authenticate.Dtos;
using Application.Common.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Authenticate.Commands
{
    public class LoginCommand : IRequest<LoginDto>
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginDto>
    {
        private readonly IIdentityService _identityService;

        public LoginCommandHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<LoginDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var result = await _identityService.AuthenticateAsync(request.UserName, request.Password);
            var resultDto = new LoginDto()
            {
                AccessToken = result.token,
                ValidTo = result.validTo
            };
            return resultDto;
        }
    }
}
