using Application.Common.Interfaces;
using MediatR;

namespace Application.Authenticate.Commands
{
    public class RegisterUserCommand : IRequest<bool>
    {
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalNumber { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public DateTime? BirthDate { get; set; }
    }

    public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, bool>
    {
        private readonly IIdentityService _identityService;

        public RegisterUserCommandHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<bool> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
        {
            var result = await _identityService.CreateUserAsync(
                                    request.UserName, 
                                    request.FirstName, 
                                    request.LastName, 
                                    request.Password, 
                                    request.PersonalNumber,
                                    request.BirthDate
                                    );
            return result;
        }
    }
}
