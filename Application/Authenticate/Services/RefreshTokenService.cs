using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Application.Authenticate.Services
{
    /// <summary>
    /// Issues, rotates and revokes refresh tokens.
    /// <para>
    /// Lives in <c>Application</c> for the same reason <c>OtpService</c> does: every collaborator
    /// below is an abstraction, and the one piece that is mechanism — <c>IRefreshTokenHasher</c>,
    /// which is a keyed HMAC — stays in <c>Infrastructure</c>. How long a session lasts and what
    /// happens when a spent token comes back are use case decisions, not technical ones.
    /// </para>
    /// </summary>
    public class RefreshTokenService : IRefreshTokenService
    {
        /// <summary>
        /// 32 bytes. The token is the entire credential and it is never typed by a human, so there
        /// is no reason to make it shorter than the hash that stores it.
        /// </summary>
        private const int TokenBytes = 32;

        private readonly IRefreshTokenRepository _refreshTokens;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRefreshTokenHasher _hasher;
        private readonly IDateTime _dateTime;
        private readonly IOptionsMonitor<RefreshTokenOptions> _optionsMonitor;
        private readonly ILogger<RefreshTokenService> _logger;

        public RefreshTokenService(
            IRefreshTokenRepository refreshTokens,
            IUnitOfWork unitOfWork,
            IRefreshTokenHasher hasher,
            IDateTime dateTime,
            IOptionsMonitor<RefreshTokenOptions> optionsMonitor,
            ILogger<RefreshTokenService> logger)
        {
            _refreshTokens = refreshTokens;
            _unitOfWork = unitOfWork;
            _hasher = hasher;
            _dateTime = dateTime;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        public async Task<RefreshTokenIssue> IssueAsync(string userId, CancellationToken cancellationToken = default)
        {
            var options = _optionsMonitor.CurrentValue;
            var now = _dateTime.UtcNow;

            var issued = await AppendAsync(
                Guid.NewGuid(),
                userId,
                now,
                now.AddDays(options.AbsoluteExpireDays),
                options,
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return issued;
        }

        public async Task<RefreshTokenIssue> RotateAsync(string token, CancellationToken cancellationToken = default)
        {
            var now = _dateTime.UtcNow;
            var options = _optionsMonitor.CurrentValue;

            var existing = await _refreshTokens.GetByHashAsync(_hasher.Hash(token), cancellationToken);

            // An unknown hash and a rejected one get the same treatment throughout this method:
            // one exception, one message. Telling a caller which of the two it was turns the
            // endpoint into an oracle for stolen tokens.
            if (existing is null)
                throw new InvalidCredentialsException("InvalidRefreshToken");

            if (existing.Status != RefreshTokenStatus.Active)
            {
                // Captured before the revocation below overwrites it. Consumed means a spent token
                // was replayed; Revoked means someone is still trying against a session that has
                // already been killed. Logging it afterwards reported "Revoked" every time and
                // threw away the only detail worth having.
                var presentedStatus = existing.Status;

                // The token was already spent or already revoked, and there is no way to tell
                // whether this call is the thief or the victim catching up. Killing the lineage
                // resolves it the only safe way: both parties have to log in again.
                await RevokeSessionAsync(existing, now, cancellationToken);

                // Committed before the throw, not after: an exception thrown over an uncommitted
                // revocation leaves the stolen lineage live, which is the whole thing this branch
                // exists to prevent.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Refresh token reuse detected for session {SessionId} in status {TokenStatus}; the session has been revoked.",
                    existing.SessionId,
                    presentedStatus);

                throw new InvalidCredentialsException("InvalidRefreshToken");
            }

            if (existing.HasExpired(now))
                throw new InvalidCredentialsException("InvalidRefreshToken");

            // Tracked, so mutating it is what persists — no Update call.
            existing.Consume(now);

            // The replacement inherits both the lineage and its deadline, so rotation cannot
            // extend a session past the ceiling the original login set.
            var issued = await AppendAsync(
                existing.SessionId,
                existing.UserId!,
                now,
                existing.SessionExpiresAt,
                options,
                cancellationToken);

            // One save for the consume and the insert together: a commit that stored the
            // replacement without spending the original would leave two live tokens in one lineage.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return issued;
        }

        public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default)
        {
            var existing = await _refreshTokens.GetByHashAsync(_hasher.Hash(token), cancellationToken);

            // Silent for an unknown token. Logout is not an authorization decision — a caller who
            // cannot produce a real token has nothing to revoke, and saying so would confirm which
            // tokens exist.
            if (existing is null)
                return;

            await RevokeSessionAsync(existing, _dateTime.UtcNow, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Adds one token to a lineage. Does not save — both callers commit alongside other changes.
        /// </summary>
        private async Task<RefreshTokenIssue> AppendAsync(
            Guid sessionId,
            string userId,
            DateTime now,
            DateTime sessionExpiresAt,
            RefreshTokenOptions options,
            CancellationToken cancellationToken)
        {
            var token = GenerateToken();

            // Never longer than the session ceiling: the entity rejects that combination outright,
            // and clamping here is what keeps a long ExpireDays from breaking the last rotation
            // before a session ends.
            var expiresAt = Min(now.AddDays(options.ExpireDays), sessionExpiresAt);

            var entity = RefreshToken.Create(
                sessionId,
                userId,
                _hasher.Hash(token),
                expiresAt,
                sessionExpiresAt,
                now);

            await _refreshTokens.AddAsync(entity, cancellationToken);

            return new RefreshTokenIssue(userId, token, entity.ExpiresAt);
        }

        private async Task RevokeSessionAsync(RefreshToken member, DateTime now, CancellationToken cancellationToken)
        {
            var lineage = await _refreshTokens.ListBySessionAsync(member.SessionId, cancellationToken);

            foreach (var entity in lineage)
                entity.Revoke(now);
        }

        /// <summary>
        /// Base64url, so the value survives a URL or a header untouched — the standard alphabet's
        /// <c>+</c> and <c>/</c> do not, and a token mangled in transit is indistinguishable from a
        /// forged one.
        /// </summary>
        private static string GenerateToken() =>
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

        private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    }
}
