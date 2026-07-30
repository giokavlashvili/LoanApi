using System.ComponentModel.DataAnnotations;

namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "RefreshToken" section of appsettings.json.
    /// </summary>
    public class RefreshTokenOptions
    {
        public const string SectionName = "RefreshToken";

        /// <summary>
        /// Key for the token HMAC. Unlike <see cref="OtpOptions.Secret"/> this is not guarding
        /// against a brute force — a 256 bit random token has no candidate space worth searching —
        /// but it does mean a leaked table is useless to anyone without the key. Treat it like
        /// <see cref="JwtOptions.Secret"/>: user secrets or a key vault outside development.
        /// </summary>
        [Required, MinLength(32)]
        public string Secret { get; set; } = string.Empty;

        /// <summary>How long a single issued refresh token stays usable.</summary>
        [Range(1, 365)]
        public int ExpireDays { get; set; } = 14;

        /// <summary>
        /// Ceiling on the whole rotation chain, counted from the login that started it. Rotation
        /// hands out a fresh <see cref="ExpireDays"/> every time, so without this a session that is
        /// merely used regularly never expires at all.
        /// </summary>
        [Range(1, 365)]
        public int AbsoluteExpireDays { get; set; } = 30;
    }
}
