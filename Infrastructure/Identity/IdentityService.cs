using Application.Authenticate.Notifications;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Identity
{
    public class IdentityService : IIdentityService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IUserClaimsPrincipalFactory<ApplicationUser> _userClaimsPrincipalFactory;
        private readonly IAuthorizationService _authorizationService;
        private readonly IJwtTokenGenerator _tokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IMediator _mediator;
        private readonly ILogger<IdentityService> _logger;

        public IdentityService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUserClaimsPrincipalFactory<ApplicationUser> userClaimsPrincipalFactory,
            IAuthorizationService authorizationService,
            IJwtTokenGenerator tokenGenerator,
            IRefreshTokenService refreshTokenService,
            IMediator mediator,
            ILogger<IdentityService> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
            _authorizationService = authorizationService;
            _tokenGenerator = tokenGenerator;
            _refreshTokenService = refreshTokenService;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<string?> GetUserNameAsync(string userId)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);

            return user?.UserName;
        }

        public async Task<bool> UserExistsAsync(string userName)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == userName);

            return user is not null;
        }

        public bool IsUserNameAllowed(string userName)
        {
            var allowed = _userManager.Options.User.AllowedUserNameCharacters;

            // An empty option means Identity applies no character restriction at all.
            if (string.IsNullOrEmpty(allowed))
                return true;

            return !string.IsNullOrWhiteSpace(userName)
                && userName.All(allowed.Contains);
        }

        public async Task<bool> CreateUserAsync(string userName, string firstName, string lastName, string password, string personalNumber, string phoneNumber, DateTime? birthDate = null)
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                // No Email. The user name is a free handle here, not an address, so assigning it
                // to Email produced "Email 'eqsel3' is invalid" from Identity's own validator.
                // Accounts are verified by phone and identified by PersonalNumber; see the
                // RequireUniqueEmail note in Infrastructure/Common/Extensions/ConfigureServices.cs.
                BirthDate= birthDate,
                FirstName= firstName,
                LastName= lastName,
                PersonalNumber=personalNumber,
                PhoneNumber = phoneNumber,
                // The handler only runs once OtpVerificationBehavior has redeemed a code sent to
                // this number, so it is confirmed by construction. Leaving it false would ask the
                // user to prove the same number twice.
                PhoneNumberConfirmed = true,
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                // The identity codes are the only record of *why* registration was refused: the
                // response carries the descriptions, and nothing else logs them. Not named
                // {UserName}, which the SQL sink would bind to the Logs.UserName column reserved
                // for the authenticated caller -- and registration is anonymous.
                _logger.LogWarning(
                    "Creating user {AttemptedUserName} failed: {IdentityErrors}",
                    userName,
                    string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));

                throw result.ToValidationException();
            }

            // Inside the success branch: published unconditionally, this announced a user that
            // CreateAsync had just refused to create, and the handler logged it.
            //
            // Published directly rather than through the domain event path because that path is
            // closed to ApplicationUser: DispatchDomainEvents scans ChangeTracker.Entries<BaseEntity>()
            // and an IdentityUser can never be one. See the notification's own remarks.
            await _mediator.Publish(new UserRegisteredNotification(userName, firstName, lastName));

            return true;
        }

        public async Task<bool> IsInRoleAsync(string userId, string role)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);

            return user != null && await _userManager.IsInRoleAsync(user, role);
        }

        public async Task<bool> AuthorizeAsync(string userId, string policyName)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return false;
            }

            var principal = await _userClaimsPrincipalFactory.CreateAsync(user);

            var result = await _authorizationService.AuthorizeAsync(principal, policyName);

            return result.Succeeded;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);

            return user == null ? false : await DeleteUserAsync(user);
        }

        private async Task<bool> DeleteUserAsync(ApplicationUser user)
        {
            var result = await _userManager.DeleteAsync(user);

            return result.Succeeded;
        }

        public async Task<AuthenticationResult> AuthenticateAsync(string usernName, string password, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByNameAsync(usernName);

            // One exception for both failures, and no logging of which: distinguishing "no such
            // user" from "wrong password" turns this endpoint into a user name oracle.
            if (user == null || !await _userManager.CheckPasswordAsync(user, password))
                throw new InvalidCredentialsException();

            return await IssueAsync(user, cancellationToken);
        }

        public async Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            // Rotation happens first and commits on its own, so a token presented twice is already
            // spent by the time anything else runs. The cost is that a user deleted in the meantime
            // leaves the caller holding a burned token — a re-login, which is the right outcome.
            var rotated = await _refreshTokenService.RotateAsync(refreshToken, cancellationToken);

            var user = await _userManager.FindByIdAsync(rotated.UserId);

            // The session outlived the account. Same opaque failure as every other refresh
            // rejection: the caller learns only that it has to log in again.
            if (user is null)
            {
                _logger.LogWarning(
                    "Refresh token rotated for user {RotatedUserId}, which no longer exists.",
                    rotated.UserId);

                throw new InvalidCredentialsException("InvalidRefreshToken");
            }

            // Re-read rather than carried in the refresh token: roles are claims, and a token minted
            // from a stale copy would keep a revoked role alive for the life of the session.
            var userRoles = await _userManager.GetRolesAsync(user);

            var (token, validTo) = _tokenGenerator.Generate(user.Id, user.UserName!, userRoles);

            return new AuthenticationResult(token, validTo, rotated.Token, rotated.ExpiresAt);
        }

        /// <summary>
        /// Mints an access token and opens a refresh token session for a user whose credentials have
        /// already been accepted.
        /// </summary>
        private async Task<AuthenticationResult> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            var userRoles = await _userManager.GetRolesAsync(user);

            var (token, validTo) = _tokenGenerator.Generate(user.Id, user.UserName!, userRoles);

            var refreshToken = await _refreshTokenService.IssueAsync(user.Id, cancellationToken);

            return new AuthenticationResult(token, validTo, refreshToken.Token, refreshToken.ExpiresAt);
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return await Task.FromResult<User?>(null);
            else
                return new User()
                {
                    Id = user.Id,
                    BirthDate= user.BirthDate,
                    FirstName= user.FirstName,
                    LastName= user.LastName,
                    PersonalNumber= user.PersonalNumber,
                    PhoneNumber = user.PhoneNumber,
                    UserName = user.UserName
                };
        }
    }
}