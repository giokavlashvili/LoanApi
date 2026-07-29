namespace Application.Common.Interfaces
{
    /// <summary>
    /// Turns a code into the value stored on the challenge. Keyed by the challenge id so a hash
    /// lifted from one row cannot be replayed against another.
    /// </summary>
    public interface IOtpCodeHasher
    {
        string Hash(Guid challengeId, string code);

        /// <summary>
        /// Fingerprints the payload a challenge was issued for, so the confirming call cannot
        /// swap in a different one.
        /// </summary>
        string HashRequest(object request);
    }
}
