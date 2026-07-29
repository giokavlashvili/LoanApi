using Application.Common.Interfaces;
using Application.Common.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.Services
{
    /// <summary>
    /// HMAC-SHA256 over the challenge id and the code, keyed by <c>Otp:Secret</c>.
    /// <para>
    /// A plain digest would be no protection at all here: six digits is a million candidates, so
    /// anyone holding a copy of the table reverses every live code in under a second. The key is
    /// what makes that infeasible, and binding in the challenge id means a hash lifted from one
    /// row does not match any other.
    /// </para>
    /// </summary>
    public class HmacOtpCodeHasher : IOtpCodeHasher
    {
        /// <summary>
        /// Removed before a request is fingerprinted. They are absent on the call that asks for
        /// a code and present on the call that answers, and the two have to agree.
        /// </summary>
        private static readonly HashSet<string> OtpMembers = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(Application.Common.Otp.IRequireOtpVerification.ChallengeId),
            nameof(Application.Common.Otp.IRequireOtpVerification.OtpCode)
        };

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false
        };

        private readonly IOptionsMonitor<OtpOptions> _optionsMonitor;

        public HmacOtpCodeHasher(IOptionsMonitor<OtpOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public string Hash(Guid challengeId, string code)
        {
            return Compute($"{challengeId:N}:{code}");
        }

        public string HashRequest(object request)
        {
            if (request is null)
                return Compute(string.Empty);

            try
            {
                var node = JsonNode.Parse(JsonSerializer.Serialize(request, request.GetType(), SerializerOptions));

                if (node is JsonObject obj)
                {
                    foreach (var property in obj.ToList())
                    {
                        if (OtpMembers.Contains(property.Key))
                            obj.Remove(property.Key);
                    }
                }

                return Compute(node?.ToJsonString(SerializerOptions) ?? string.Empty);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // A command that will not serialize still has to be gated, and a hash that never
                // matches is the safe direction to fail: confirmation is refused, not skipped.
                return Compute(Guid.NewGuid().ToString("N"));
            }
        }

        private string Compute(string value)
        {
            var secret = _optionsMonitor.CurrentValue.Secret;

            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Otp:Secret is not configured. One time passwords cannot be issued without it.");

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}
