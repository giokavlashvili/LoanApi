using Application.Common.Models;

namespace Application.Common.Interfaces
{
    public interface IIdentityService : IUserService
    {
        Task<bool> UserExistsAsync(string userName);

        /// <summary>
        /// Whether ASP.NET Identity would accept this user name's characters. Exists so the
        /// FluentValidation layer can enforce the rule <em>before</em> the request reaches
        /// <c>OtpVerificationBehavior</c>: anything left for <c>CreateUserAsync</c> to reject is
        /// only discovered after a code has been sent and spent, and because a challenge is bound
        /// to its payload, correcting the input then needs a whole new code.
        /// <para>
        /// Reads <c>UserOptions.AllowedUserNameCharacters</c> rather than restating it, so the
        /// validator cannot drift from what Identity actually enforces.
        /// </para>
        /// </summary>
        bool IsUserNameAllowed(string userName);

        Task<bool> AuthorizeAsync(string userId, string policyName);

        Task<bool> CreateUserAsync(string userName, string firstName, string lastName, string password, string personalNumber, string phoneNumber, DateTime? birthDate = null);

        Task<bool> DeleteUserAsync(string userId);

        /// <summary>
        /// Verifies credentials, then issues an access token and opens a refresh token session.
        /// Throws <see cref="Exceptions.InvalidCredentialsException"/> for both an unknown user name
        /// and a wrong password.
        /// </summary>
        Task<AuthenticationResult> AuthenticateAsync(string usernName, string password, CancellationToken cancellationToken = default);

        /// <summary>
        /// Trades a refresh token for a new pair, rotating the refresh token in the process. The
        /// access token is re-minted from the user's <em>current</em> roles, which is what makes a
        /// role change take effect within one access token lifetime rather than at next login.
        /// </summary>
        Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    }
}