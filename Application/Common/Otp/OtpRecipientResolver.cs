using Application.Common.Interfaces;
using Domain.Exceptions;

namespace Application.Common.Otp
{
    /// <inheritdoc cref="IOtpRecipientResolver"/>
    public class OtpRecipientResolver : IOtpRecipientResolver
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IUserService _userService;

        public OtpRecipientResolver(ICurrentUserService currentUserService, IUserService userService)
        {
            _currentUserService = currentUserService;
            _userService = userService;
        }

        public async Task<string> ResolveAsync(string? suppliedRecipient, CancellationToken cancellationToken = default)
        {
            // A caller only supplies a number when there is no user to look one up from —
            // registration. Everywhere else the number comes from the account, which is what stops
            // a caller redirecting their own confirmation code to a phone they control. Whether
            // supplying one is permitted at all is decided before this is called.
            if (!string.IsNullOrWhiteSpace(suppliedRecipient))
                return suppliedRecipient;

            var userId = _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                throw new DomainValidationException("OtpRecipientRequired");

            var user = await _userService.GetUserByIdAsync(userId);

            // Note this reads PhoneNumber unconditionally: the account model carries no email, so
            // there is no other number to resolve. See phase 6 task 1 before adding a channel.
            if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
                throw new DomainValidationException("OtpRecipientRequired");

            return user.PhoneNumber;
        }
    }
}
