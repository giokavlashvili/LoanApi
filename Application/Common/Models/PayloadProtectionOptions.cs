namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "PayloadProtection" section of appsettings.json.
    /// <para>
    /// Deliberately <em>not</em> <c>[Required]</c>. Encryption is only needed once a registered
    /// verifiable operation carries a <c>[SensitiveData]</c> property, and an application with no
    /// such operation should not be forced to invent a secret. The pairing is enforced where it
    /// actually matters instead: <c>VerifiableOperationRegistry.Build</c> refuses to start when a
    /// registered payload has sensitive properties and no secret is configured, so the only way to
    /// get plaintext storage is to have nothing worth encrypting.
    /// </para>
    /// </summary>
    public class PayloadProtectionOptions
    {
        public const string SectionName = "PayloadProtection";

        /// <summary>
        /// Key for the payload cipher. Treat it exactly like <c>Otp:Secret</c> and
        /// <c>JWT:Secret</c> — user secrets or a key vault outside development, never a value
        /// committed to a base configuration file.
        /// <para>
        /// <strong>Rotating this is destructive.</strong> There is one key and no key history, so
        /// anything already encrypted under the old value becomes permanently unreadable. Pending
        /// payloads live minutes and are nulled the moment an operation completes, so those are
        /// survivable; <c>PendingOperation.ResultPayload</c> is <em>never</em> nulled, so encrypted
        /// results are lost for good. If that matters, this is the point at which the
        /// <c>IDataProtector</c> option in <c>docs/plans/08-sensitive-payload-encryption.md</c>
        /// becomes worth its migration.
        /// </para>
        /// </summary>
        public string? Secret { get; set; }
    }
}
