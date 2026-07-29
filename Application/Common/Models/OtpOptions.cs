using System.ComponentModel.DataAnnotations;

namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "Otp" section of appsettings.json.
    /// </summary>
    public class OtpOptions
    {
        public const string SectionName = "Otp";

        /// <summary>Digits in the generated code.</summary>
        [Range(4, 10)]
        public int CodeLength { get; set; } = 6;

        /// <summary>How long an issued code stays usable.</summary>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Wrong guesses allowed before the challenge locks. A 6 digit code is only a million
        /// possibilities, so this is what makes it safe rather than the code length.
        /// </summary>
        [Range(1, 20)]
        public int MaxAttempts { get; set; } = 5;

        /// <summary>
        /// Challenges one recipient may be issued per hour, across a single purpose. Stops the
        /// endpoint being used to pump SMS at an arbitrary number at someone else's expense.
        /// </summary>
        public int MaxPerRecipientPerHour { get; set; } = 5;

        /// <summary>Minimum gap between two challenges for the same recipient and purpose.</summary>
        public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Key for the code HMAC. Codes are short enough that an unkeyed digest of a leaked
        /// table would be reversed instantly; this is what makes the stored hash worth storing.
        /// Treat it like JWT:Secret — user secrets or a key vault outside development.
        /// </summary>
        [Required]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Template for the outgoing message. <c>{0}</c> is the code.</summary>
        public string MessageTemplate { get; set; } = "Your verification code is {0}.";

        /// <summary>
        /// When set, every issued challenge uses this code instead of a random one — the rest of
        /// the flow (hashing, expiry, attempt limit, purpose binding, request binding) still runs
        /// exactly as it does with a real code, so a test suite or a manual QA pass can always
        /// answer with the same value instead of reading it out of a log or an SMS.
        /// <para>
        /// Left null in <c>appsettings.json</c> and set only in
        /// <c>appsettings.Development.json</c>, the same way <c>UseInMemoryDatabase</c> is
        /// environment scoped — never set this in a base or production configuration file.
        /// <see cref="Infrastructure.Services.OtpService"/> logs a warning on every issue while
        /// it is set, so an accidental production value cannot go unnoticed.
        /// </para>
        /// </summary>
        public string? StaticCode { get; set; }
    }
}
