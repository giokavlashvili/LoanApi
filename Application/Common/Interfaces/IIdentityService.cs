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

        Task<(string token, DateTime validTo)> AuthenticateAsync(string usernName, string password);
    }
}