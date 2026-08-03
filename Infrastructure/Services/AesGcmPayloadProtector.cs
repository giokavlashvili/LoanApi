using Application.Common.Interfaces;
using Application.Common.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services
{
    /// <summary>
    /// AES-256-GCM over a key derived from <c>PayloadProtection:Secret</c>.
    /// <para>
    /// GCM rather than CBC because it is <em>authenticated</em>: decryption fails loudly on a value
    /// that was altered in the database, instead of returning plausible-looking garbage that then
    /// gets deserialized into a command and executed. That property is the whole reason this is
    /// worth doing at the field level rather than encrypting the row and hoping.
    /// </para>
    /// <para>
    /// A fresh random nonce is generated per call and stored alongside the ciphertext. This is the
    /// one rule GCM cannot survive breaking — reusing a nonce under the same key leaks the
    /// relationship between two plaintexts and can expose the authentication key. Never derive the
    /// nonce from the value, and never cache one.
    /// </para>
    /// <para>
    /// Chosen over <c>IDataProtector</c> deliberately; see
    /// <c>docs/plans/08-sensitive-payload-encryption.md</c> for what that trade costs (no key
    /// history, so rotating the secret is destructive) and when to revisit it.
    /// </para>
    /// </summary>
    public class AesGcmPayloadProtector : IPayloadProtector
    {
        /// <summary>
        /// Marks the format so a future algorithm change is a clean failure rather than a garbled
        /// decrypt. Anything not carrying it was written by a scheme this build does not know.
        /// </summary>
        private const string VersionPrefix = "v1:";

        private const int NonceSize = 12;   // AesGcm.NonceByteSizes.MaxSize
        private const int TagSize = 16;     // AesGcm.TagByteSizes.MaxSize

        private readonly IOptionsMonitor<PayloadProtectionOptions> _optionsMonitor;

        public AesGcmPayloadProtector(IOptionsMonitor<PayloadProtectionOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_optionsMonitor.CurrentValue.Secret);

        public string Protect(string plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);

            var key = DeriveKey();
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            // nonce || tag || ciphertext. Both prefixes are fixed width, so the reader needs no
            // length header to split them back apart.
            var envelope = new byte[NonceSize + TagSize + ciphertext.Length];
            nonce.CopyTo(envelope, 0);
            tag.CopyTo(envelope, NonceSize);
            ciphertext.CopyTo(envelope, NonceSize + TagSize);

            return VersionPrefix + Convert.ToBase64String(envelope);
        }

        public string Unprotect(string protectedValue)
        {
            ArgumentNullException.ThrowIfNull(protectedValue);

            if (!protectedValue.StartsWith(VersionPrefix, StringComparison.Ordinal))
                throw new CryptographicException("Protected payload is missing the expected version prefix.");

            byte[] envelope;

            try
            {
                envelope = Convert.FromBase64String(protectedValue[VersionPrefix.Length..]);
            }
            catch (FormatException ex)
            {
                // Surfaced as a CryptographicException so every failure to read a stored payload —
                // wrong key, altered row, corrupted text — reaches the caller as one kind of
                // problem. VerifiableOperationService catches exactly that and fails closed.
                throw new CryptographicException("Protected payload is not valid base64.", ex);
            }

            if (envelope.Length < NonceSize + TagSize)
                throw new CryptographicException("Protected payload is shorter than its own header.");

            var key = DeriveKey();
            var nonce = envelope.AsSpan(0, NonceSize);
            var tag = envelope.AsSpan(NonceSize, TagSize);
            var ciphertext = envelope.AsSpan(NonceSize + TagSize);
            var plaintextBytes = new byte[ciphertext.Length];

            // Throws AuthenticationTagMismatchException (a CryptographicException) if the key is
            // wrong or the value was tampered with. Deliberately not caught here: the distinction
            // is not one the caller can act on differently.
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }

            return Encoding.UTF8.GetString(plaintextBytes);
        }

        /// <summary>
        /// SHA-256 of the configured secret, because AES-256 needs exactly 32 bytes and the secret
        /// is free-form text. This is a digest, not a password KDF — it adds no work factor, so the
        /// secret itself must carry the entropy. Generate it, do not think of one.
        /// </summary>
        private byte[] DeriveKey()
        {
            var secret = _optionsMonitor.CurrentValue.Secret;

            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException(
                    "PayloadProtection:Secret is not configured. A verifiable operation carrying [SensitiveData] " +
                    "cannot store its payload without it.");

            return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }
    }
}
