using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Common.Models;
using Domain.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        public IdentityService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUserClaimsPrincipalFactory<ApplicationUser> userClaimsPrincipalFactory,
            IAuthorizationService authorizationService,
            IConfiguration config,
            IDateTime dateTime,
            IMediator mediator)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _userClaimsPrincipalFactory = userClaimsPrincipalFactory;
            _authorizationService = authorizationService;
            _config = config;
            _dateTime = dateTime;
            _mediator = mediator;
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

        public async Task<bool> CreateUserAsync(string userName, string firstName, string lastName, string password, string personalNumber, DateTime? birthDate = null)
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                BirthDate= birthDate,
                FirstName= firstName,
                LastName= lastName,
                PersonalNumber=personalNumber,
            };

            var result = await _userManager.CreateAsync(user, password);

            await _mediator.Publish(new UserCreatedEvent(userName));

            return result.Succeeded;
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
                expires: _dateTime.Now.AddMinutes(int.Parse(_config["JWT:ExpireMinutes"])),
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
                    UserName = user.UserName
                };
        }
    }
}