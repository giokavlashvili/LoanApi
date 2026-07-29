using Domain.Common;
using Domain.Enums;
using Domain.Events;
using Domain.Exceptions;
using System.Security.Cryptography;

namespace Domain.Entities
{
    /// <summary>
    /// One issued one time password and everything that decides whether it may still be used:
    /// expiry, attempt budget, single use, and the payload it was issued for. The code itself is
    /// never stored — only a hash produced outside the domain, so no crypto lives in here.
    /// </summary>
    public class OtpVerification : BaseAuditableEntity
    {
        /// <summary>
        /// The handle handed to the client. <see cref="BaseEntity.Id"/> is a sequential int and
        /// would let a caller walk other people's challenges, so it never leaves the server.
        /// </summary>
        public Guid ChallengeId { get; private set; }

        /// <summary>
        /// Scopes a code to one operation, so a code texted for registration cannot be replayed
        /// against a loan approval. Defaults to the command type name.
        /// </summary>
        public string? Purpose { get; private set; }

        /// <summary>Phone number the code was sent to.</summary>
        public string? Recipient { get; private set; }

        /// <summary>Null while the challenge belongs to a user that does not exist yet.</summary>
        public string? UserId { get; private set; }

        public string? CodeHash { get; private set; }

        /// <summary>
        /// Hash of the request the challenge was issued for. Without it the caller could send a
        /// harmless payload, confirm it over SMS, then resubmit a different one with the same code.
        /// </summary>
        public string? RequestHash { get; private set; }

        public DateTime ExpiresAt { get; private set; }
        public int AttemptCount { get; private set; }
        public int MaxAttempts { get; private set; }
        public DateTime? ConsumedAt { get; private set; }
        public OtpVerificationStatus Status { get; private set; }

        /// <summary>
        /// Optimistic concurrency token. Two concurrent writers to the same row make the second
        /// SaveChanges throw DbUpdateConcurrencyException instead of silently overwriting.
        /// </summary>
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// <paramref name="challengeId"/> is supplied rather than generated here because the
        /// code hash is keyed by it, so the caller has to know it before it can hash.
        /// </summary>
        public static OtpVerification Create(
            Guid challengeId,
            string purpose,
            string recipient,
            string? userId,
            string codeHash,
            string requestHash,
            DateTime expiresAt,
            int maxAttempts,
            DateTime created
            )
        {
            if (challengeId == Guid.Empty)
                throw new DomainValidationException("OtpChallengeNotFound");

            if (string.IsNullOrWhiteSpace(purpose))
                throw new DomainValidationException("InvalidOtpPurpose");

            if (string.IsNullOrWhiteSpace(recipient))
                throw new DomainValidationException("OtpRecipientRequired");

            if (string.IsNullOrWhiteSpace(codeHash))
                throw new DomainValidationException("InvalidOtpCode");

            if (expiresAt <= created)
                throw new DomainValidationException("InvalidOtpLifetime");

            if (maxAttempts <= 0)
                throw new DomainValidationException("InvalidOtpMaxAttempts");

            var entity = new OtpVerification()
            {
                ChallengeId = challengeId,
                Purpose = purpose,
                Recipient = recipient,
                UserId = userId,
                CodeHash = codeHash,
                RequestHash = requestHash,
                ExpiresAt = expiresAt,
                MaxAttempts = maxAttempts,
                Status = OtpVerificationStatus.Pending,
                Created = created,
                CreatedBy = userId
            };

            entity.AddDomainEvent(new OtpChallengeCreatedEvent(entity));

            return entity;
        }

        /// <summary>
        /// Consumes the challenge. Both hashes are computed by the caller — the domain only
        /// compares them and applies the rules.
        /// </summary>
        public void Verify(string candidateHash, string requestHash, DateTime now)
        {
            if (Status != OtpVerificationStatus.Pending)
                throw new DomainValidationException("OtpAlreadyUsed");

            if (now >= ExpiresAt)
            {
                Status = OtpVerificationStatus.Expired;
                throw new DomainValidationException("OtpExpired");
            }

            if (AttemptCount >= MaxAttempts)
            {
                Status = OtpVerificationStatus.Locked;
                throw new DomainValidationException("OtpLocked");
            }

            // Counted before the comparison, so a caller cannot buy extra guesses by sending a
            // request that fails a later check.
            AttemptCount++;

            if (!HashesMatch(RequestHash, requestHash))
                throw new DomainValidationException("OtpRequestMismatch");

            if (!HashesMatch(CodeHash, candidateHash))
            {
                if (AttemptCount >= MaxAttempts)
                    Status = OtpVerificationStatus.Locked;

                throw new DomainValidationException("InvalidOtpCode");
            }

            Status = OtpVerificationStatus.Verified;
            ConsumedAt = now;
            LastModified = now;

            this.AddDomainEvent(new OtpVerifiedEvent(this));
        }

        /// <summary>
        /// Compares two Base64-encoded hashes in constant time, so a timing signal cannot leak
        /// how many leading bytes matched. A malformed candidate is treated as a mismatch, not a
        /// FormatException escaping to a 500.
        /// </summary>
        private static bool HashesMatch(string? stored, string candidate)
        {
            if (string.IsNullOrEmpty(stored))
                return false;

            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(stored), Convert.FromBase64String(candidate));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Retires a still pending challenge, so re-issuing for the same operation cannot leave
        /// two live codes for one recipient.
        /// </summary>
        public void Invalidate(DateTime now)
        {
            if (Status != OtpVerificationStatus.Pending)
                return;

            Status = OtpVerificationStatus.Expired;
            LastModified = now;
        }
    }
}
