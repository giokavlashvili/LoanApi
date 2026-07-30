using Application.Common.Interfaces;
using Application.Common.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Infrastructure.Identity
{
    /// <summary>
    /// The signing half of authentication, split out of <see cref="IdentityService"/> so that
    /// service is left doing user lifecycle and authorization only.
    /// </summary>
    public sealed class JwtTokenGenerator : IJwtTokenGenerator
    {
        private readonly IOptionsMonitor<JwtOptions> _jwtOptions;
        private readonly IDateTime _dateTime;

        public JwtTokenGenerator(IOptionsMonitor<JwtOptions> jwtOptions, IDateTime dateTime)
        {
            _jwtOptions = jwtOptions;
            _dateTime = dateTime;
        }

        public (string Token, DateTime ValidTo) Generate(string userId, string userName, IEnumerable<string> roles)
        {
            // Read per call rather than captured in the constructor: this is a singleton, and
            // CurrentValue is what lets a rotated secret take effect without a restart.
            var options = _jwtOptions.CurrentValue;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, userName),
                new(ClaimTypes.NameIdentifier, userId),
                // A fresh id per token, so one token can be named in a log or a deny list without
                // that identifier also matching every other token issued to the same user.
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));

            var token = new JwtSecurityToken(
                expires: _dateTime.UtcNow.AddMinutes(options.ExpireMinutes),
                claims: claims,
                signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

            return (new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
        }
    }
}
