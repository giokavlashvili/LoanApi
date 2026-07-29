using Application.Common.Interfaces;
using Application.Common.Logging;
using Application.Common.Otp;
using MediatR;
using System.Text.Json.Serialization;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.Authenticate.Commands
{
    /// <summary>
    /// Two step registration. Implementing <see cref="IRequireOtpVerification"/> and declaring
    /// the three members below is the whole of it — no second command, no second endpoint, no
    /// pending-registration state to reconcile. The account is created only on the call that
    /// carries a good code, so an unconfirmed number never leaves a half-made user behind.
    /// </summary>
    public class RegisterUserCommand : IRequest<bool>, IRequireOtpVerification
    {
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public DateTime? BirthDate { get; set; }

        public Guid? ChallengeId { get; set; }

        [SensitiveData]
        public string? OtpCode { get; set; }

        /// <summary>
        /// The only place a command supplies its own recipient: there is no account yet to read
        /// a number from. Everywhere else leaving this null is what stops a caller pointing a
        /// confirmation at a phone they control.
        /// <para>
        /// Ignored by the serializer so it does not surface in the OpenAPI document and the
        /// generated client as a settable field — it is derived from PhoneNumber, and an input
        /// callers can fill in but that is silently discarded is worse than no input at all.
        /// </para>
        /// </summary>
        [JsonIgnore]
        public string? OtpRecipient => PhoneNumber;
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
                                    request.PhoneNumber,
                                    request.BirthDate
                                    );
            return result;
        }
    }
}
