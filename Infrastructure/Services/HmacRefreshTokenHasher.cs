using Application.Common.Interfaces;
using Application.Common.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services
{
    /// <summary>
    /// HMAC-SHA256 over the raw refresh token, keyed by <c>RefreshToken:Secret</c>.
    /// <para>
    /// The argument for the key differs from <see cref="HmacOtpCodeHasher"/>'s. A six digit code is
    /// a million candidates, so an unkeyed digest is reversible on sight; a 256 bit random token is
    /// not, and a plain SHA-256 of it would already be safe against a search. The key buys
    /// something else: it lives outside the database, so a leaked table alone cannot be turned into
    /// working tokens.
    /// </para>
    /// </summary>
    public class HmacRefreshTokenHasher : IRefreshTokenHasher
    {
        private readonly IOptionsMonitor<RefreshTokenOptions> _optionsMonitor;

        public HmacRefreshTokenHasher(IOptionsMonitor<RefreshTokenOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public string Hash(string token)
        {
            var secret = _optionsMonitor.CurrentValue.Secret;

            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("RefreshToken:Secret is not configured. Refresh tokens cannot be issued without it.");

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));
        }
    }
}
