using Domain.Common;
using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    /// <summary>
    /// One issued refresh token and everything that decides whether it may still be spent: its own
    /// expiry, the absolute ceiling of the session it belongs to, and single use. The token itself
    /// is never stored — only a hash produced outside the domain, so no crypto lives in here.
    /// <para>
    /// Rotation means a session is a <em>chain</em> of these rows sharing one
    /// <see cref="SessionId"/>, at most one of them <see cref="RefreshTokenStatus.Active"/>. That
    /// is what makes both revocation targets possible: one token, or the whole lineage.
    /// </para>
    /// </summary>
    public class RefreshToken : BaseAuditableEntity, IAggregateRoot, IHasRowVersion
    {
        /// <summary>
        /// The rotation lineage — every replacement inherits it. Reuse of a spent token cannot be
        /// attributed to the attacker or the victim, so the response is to revoke all of them.
        /// </summary>
        public Guid SessionId { get; private set; }

        public string? UserId { get; private set; }

        public string? TokenHash { get; private set; }

        public DateTime ExpiresAt { get; private set; }

        /// <summary>
        /// The session's absolute deadline, copied unchanged into every replacement. Without it,
        /// rotation renews the expiry forever and the session never actually ends.
        /// </summary>
        public DateTime SessionExpiresAt { get; private set; }

        public DateTime? ConsumedAt { get; private set; }

        public DateTime? RevokedAt { get; private set; }

        /// <summary>Audit only; nothing reads these yet.</summary>
        public string? CreatedByIp { get; private set; }

        public string? UserAgent { get; private set; }

        public RefreshTokenStatus Status { get; private set; }

        /// <summary>
        /// Optimistic concurrency token. Two requests presenting the same token race to consume one
        /// row; the loser gets DbUpdateConcurrencyException instead of both being handed a
        /// replacement.
        /// </summary>
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// <paramref name="created"/> is supplied rather than left to the interceptor because
        /// login is an anonymous request: <c>ICurrentUserService.UserId</c> is null when
        /// <c>AuditableEntityInterceptor</c> runs, so the row would be written with no
        /// <c>CreatedBy</c> at all. Same deliberate exception <see cref="OtpVerification"/> makes,
        /// and the interceptor leaves values the entity set itself alone.
        /// </summary>
        public static RefreshToken Create(
            Guid sessionId,
            string userId,
            string tokenHash,
            DateTime expiresAt,
            DateTime sessionExpiresAt,
            DateTime created,
            string? createdByIp = null,
            string? userAgent = null)
        {
            if (sessionId == Guid.Empty)
                throw new DomainValidationException("InvalidRefreshToken");

            if (string.IsNullOrWhiteSpace(userId))
                throw new DomainValidationException("InvalidUser");

            if (string.IsNullOrWhiteSpace(tokenHash))
                throw new DomainValidationException("InvalidRefreshToken");

            if (expiresAt <= created)
                throw new DomainValidationException("InvalidRefreshTokenLifetime");

            // A per-token expiry beyond the session ceiling would be silently truncated by
            // HasExpired, which reads as a configuration bug rather than a policy.
            if (sessionExpiresAt < expiresAt)
                throw new DomainValidationException("InvalidRefreshTokenLifetime");

            return new RefreshToken()
            {
                SessionId = sessionId,
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt,
                SessionExpiresAt = sessionExpiresAt,
                Status = RefreshTokenStatus.Active,
                CreatedByIp = createdByIp,
                UserAgent = userAgent,
                Created = created,
                CreatedBy = userId
            };
        }

        /// <summary>
        /// Either deadline ends the token: its own, or the session's. Both are checked because a
        /// replacement carries a fresh <see cref="ExpiresAt"/> but the original
        /// <see cref="SessionExpiresAt"/>.
        /// </summary>
        public bool HasExpired(DateTime now) => now >= ExpiresAt || now >= SessionExpiresAt;

        /// <summary>
        /// Spends the token. Single use is the whole mechanism: a caller that presents an already
        /// consumed token is either replaying a stolen one or has been overtaken by a thief, and
        /// the caller is expected to treat this failure as a breach rather than a retry.
        /// </summary>
        public void Consume(DateTime now)
        {
            if (Status != RefreshTokenStatus.Active)
                throw new DomainValidationException("InvalidRefreshToken");

            Status = RefreshTokenStatus.Consumed;
            ConsumedAt = now;
            LastModified = now;
        }

        /// <summary>
        /// Retires the token, whatever state it was in. Idempotent, and deliberately applies to
        /// consumed rows too: revoking a lineage has to close the spent links as well, or a later
        /// audit cannot tell a rotated-out token from one that was revoked.
        /// </summary>
        public void Revoke(DateTime now)
        {
            if (Status == RefreshTokenStatus.Revoked)
                return;

            Status = RefreshTokenStatus.Revoked;
            RevokedAt = now;
            LastModified = now;
        }
    }
}
