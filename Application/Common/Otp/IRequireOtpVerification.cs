namespace Application.Common.Otp
{
    /// <summary>
    /// Marks a command as requiring phone confirmation before its handler runs.
    /// <see cref="Behaviors.OtpVerificationBehavior{TRequest, TResponse}"/> does the rest:
    /// a request arriving without a code gets a challenge issued and is rejected, a request
    /// carrying one is verified and passed through.
    /// <para>
    /// Adding two step verification to a new operation is implementing this interface and
    /// declaring the two properties. Everything else — issuing, throttling, expiry, attempt
    /// limits, redaction — is already wired.
    /// </para>
    /// </summary>
    public interface IRequireOtpVerification
    {
        /// <summary>Challenge being answered. Null on the first call.</summary>
        Guid? ChallengeId { get; }

        /// <summary>
        /// The code the user received. Null on the first call. Implementations should carry
        /// <see cref="Logging.SensitiveDataAttribute"/> so a live code never reaches a log sink.
        /// </summary>
        string? OtpCode { get; }

        /// <summary>
        /// Number to text. Null means "whatever number the authenticated user is registered
        /// with", which is the right answer for every operation except registration itself —
        /// there the user does not exist yet, so the command has to supply it.
        /// </summary>
        string? OtpRecipient => null;

        /// <summary>
        /// Scopes a code to one operation. The command type name is unique per operation and
        /// needs no registry to maintain, so it is the default.
        /// </summary>
        string OtpPurpose => GetType().Name;
    }
}
