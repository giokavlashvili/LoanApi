namespace Application.Common.Interfaces
{
    /// <summary>
    /// Encrypts individual values on their way into storage. Application declares the contract,
    /// Infrastructure supplies the cipher — the same split as <c>IOtpCodeHasher</c>.
    /// <para>
    /// Deliberately <em>not</em> a hash: unlike an OTP code, a stored payload has to come back out
    /// again to be executed, so this has to be reversible where <c>IOtpCodeHasher</c> must not be.
    /// </para>
    /// </summary>
    public interface IPayloadProtector
    {
        /// <summary>True when a secret is configured and this protector can actually be used.</summary>
        bool IsConfigured { get; }

        string Protect(string plaintext);

        /// <summary>
        /// Reverses <see cref="Protect"/>. Throws
        /// <see cref="System.Security.Cryptography.CryptographicException"/> if the value was
        /// written under a different key, or altered since — an authenticated cipher cannot tell
        /// those apart, and both mean the same thing to a caller: this payload cannot be trusted
        /// to execute.
        /// </summary>
        string Unprotect(string protectedValue);
    }
}
