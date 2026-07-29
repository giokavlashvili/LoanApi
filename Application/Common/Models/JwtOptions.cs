using System.ComponentModel.DataAnnotations;

namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "JWT" section of appsettings.json.
    /// </summary>
    public class JwtOptions
    {
        public const string SectionName = "JWT";

        /// <summary>
        /// Key for the token signature. <c>MinLength(32)</c> is not arbitrary: HMAC-SHA256 with a
        /// key shorter than the hash output is accepted by <c>SymmetricSecurityKey</c> but
        /// weakens the signature.
        /// </summary>
        [Required, MinLength(32)]
        public string Secret { get; set; } = string.Empty;

        /// <summary>How long an issued token stays valid.</summary>
        [Range(1, 1440)]
        public int ExpireMinutes { get; set; } = 180;
    }
}
