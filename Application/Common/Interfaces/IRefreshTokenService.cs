using Application.Common.Models;

namespace Application.Common.Interfaces
{
    /// <summary>
    /// Owns the refresh token lifecycle: issue, rotate, revoke. Knows nothing about passwords or
    /// access tokens — deciding that a caller has authenticated is the caller's business, and
    /// minting a JWT belongs to <see cref="IJwtTokenGenerator"/>.
    /// </summary>
    public interface IRefreshTokenService
    {
        /// <summary>
        /// Starts a new session. Calling this is an assertion that the user has just proven who
        /// they are, since nothing here verifies anything.
        /// </summary>
        Task<RefreshTokenIssue> IssueAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Spends <paramref name="token"/> and returns its replacement. Throws
        /// <see cref="Exceptions.InvalidCredentialsException"/> for an unknown, expired, spent or
        /// revoked token, always with the same message — the response must not disclose which.
        /// </summary>
        Task<RefreshTokenIssue> RotateAsync(string token, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes the whole lineage <paramref name="token"/> belongs to. Idempotent and silent for
        /// a token that does not exist: logout has no business telling a caller whether a token was
        /// real.
        /// </summary>
        Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default);
    }
}
