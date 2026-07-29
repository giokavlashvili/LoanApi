using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Common.Models;
using Domain.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Infrastructure.Identity
{
    public class IdentityService : IIdentityService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IUserClaimsPrincipalFactory<ApplicationUser> _userClaimsPrincipalFactory;
        private readonly IAuthorizationService _authorizationService;
        private readonly IConfiguration _config;
        private readonly IDateTime _dateTime;
        private readonly IMediator _mediator;
        private readonly ILogger<IdentityService> _logger;

        public IdentityService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUserClaimsPrincipalFactory<ApplicationUser> userClaimsPrincipalFactory,
            IAuthorizationService authorizationService,
            IConfiguration config,
            IDateTime dateTime,
            IMediator mediator,
            ILogger<IdentityService> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
            _authorizationService = authorizationService;
            _config = config;
            _dateTime = dateTime;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<string?> GetUserNameAsync(string userId)
        {
            var user = await _userManager.Users.FirstAsync(u => u.Id == userId);

            return user?.UserName;
        }

        public async Task<bool> UserExistsAsync(string userName)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == userName);

            return user == null? false:true;
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
            // CreateAsync had just refused to create, and UserCreatedEventHandler logged it.
            await _mediator.Publish(new UserCreatedEvent(userName));

            return true;
        }

        public async Task<bool> IsInRoleAsync(string userId, string role)
        {
            var user = _userManager.Users.SingleOrDefault(u => u.Id == userId);

            return user != null && await _userManager.IsInRoleAsync(user, role);
        }

        public async Task<bool> AuthorizeAsync(string userId, string policyName)
        {
            var user = _userManager.Users.SingleOrDefault(u => u.Id == userId);

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
            var user = _userManager.Users.SingleOrDefault(u => u.Id == userId);

            return user == null ? false : await DeleteUserAsync(user);
        }

        private async Task<bool> DeleteUserAsync(ApplicationUser user)
        {
            var result = await _userManager.DeleteAsync(user);

            return result.Succeeded;
        }

        public async Task<(string token, DateTime validTo)> AuthenticateAsync(string usernName, string password)
        {
            var user = await _userManager.FindByNameAsync(usernName);

            if (user == null || !await _userManager.CheckPasswordAsync(user, password))
                throw new NotFoundException("User not found");

            var userRoles = await _userManager.GetRolesAsync(user);

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, usernName),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            foreach (var userRole in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, userRole));
            }

            var token = GetToken(authClaims);

            return (new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
        }

        private JwtSecurityToken GetToken(List<Claim> authClaims)
        {
            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JWT:Secret"]));

            var token = new JwtSecurityToken(
                expires: _dateTime.UtcNow.AddMinutes(int.Parse(_config["JWT:ExpireMinutes"])),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
                );

            return token;
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