using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class RefreshTokenTests
    {
        private const string UserId = "9c1d0d5e-4b6f-4c2a-8a1b-6f0d2e3a4b5c";
        private const string TokenHash = "dG9rZW4taGFzaA==";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        private static RefreshToken CreateToken(Guid? sessionId = null, DateTime? expiresAt = null) =>
            RefreshToken.Create(
                sessionId ?? Guid.NewGuid(),
                UserId,
                TokenHash,
                expiresAt ?? Now.AddDays(14),
                Now.AddDays(30),
                Now);

        [Test]
        public void CreateRefreshToken_WithValidParameters_StartsActiveAndStampsItsOwnAudit()
        {
            var sessionId = Guid.NewGuid();

            var entity = RefreshToken.Create(sessionId, UserId, TokenHash, Now.AddDays(14), Now.AddDays(30), Now);

            Assert.That(entity.Status, Is.EqualTo(RefreshTokenStatus.Active));
            Assert.That(entity.SessionId, Is.EqualTo(sessionId));
            Assert.That(entity.ConsumedAt, Is.Null);
            Assert.That(entity.RevokedAt, Is.Null);

            // Set by the entity because login is anonymous: the audit interceptor has no user to
            // read, and leaves values already present alone.
            Assert.That(entity.Created, Is.EqualTo(Now));
            Assert.That(entity.CreatedBy, Is.EqualTo(UserId));
        }

        [Test]
        public void CreateRefreshToken_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => RefreshToken.Create(Guid.Empty, UserId, TokenHash, Now.AddDays(14), Now.AddDays(30), Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => RefreshToken.Create(Guid.NewGuid(), string.Empty, TokenHash, Now.AddDays(14), Now.AddDays(30), Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => RefreshToken.Create(Guid.NewGuid(), UserId, string.Empty, Now.AddDays(14), Now.AddDays(30), Now), Throws.InstanceOf<DomainValidationException>());
            // Already expired when issued.
            Assert.That(() => RefreshToken.Create(Guid.NewGuid(), UserId, TokenHash, Now, Now.AddDays(30), Now), Throws.InstanceOf<DomainValidationException>());
            // Outlives the session it belongs to.
            Assert.That(() => RefreshToken.Create(Guid.NewGuid(), UserId, TokenHash, Now.AddDays(31), Now.AddDays(30), Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void HasExpired_HonoursBothTheTokenAndTheSessionDeadline()
        {
            var entity = CreateToken();

            Assert.That(entity.HasExpired(Now.AddDays(13)), Is.False);

            // Its own expiry, well inside the session ceiling.
            Assert.That(entity.HasExpired(Now.AddDays(14)), Is.True);

            // A replacement issued near the end of a session carries a fresh 14 day expiry that
            // reaches past the ceiling, and the ceiling still ends it.
            var lateInSession = RefreshToken.Create(
                Guid.NewGuid(), UserId, TokenHash, Now.AddDays(40), Now.AddDays(40), Now);

            Assert.That(lateInSession.HasExpired(Now.AddDays(40)), Is.True);
        }

        [Test]
        public void Consume_WhenActive_MarksConsumed()
        {
            var entity = CreateToken();

            entity.Consume(Now.AddHours(1));

            Assert.That(entity.Status, Is.EqualTo(RefreshTokenStatus.Consumed));
            Assert.That(entity.ConsumedAt, Is.EqualTo(Now.AddHours(1)));
            Assert.That(entity.LastModified, Is.EqualTo(Now.AddHours(1)));
        }

        [Test]
        public void Consume_WhenAlreadyConsumed_ThrowDomainException()
        {
            var entity = CreateToken();
            entity.Consume(Now.AddHours(1));

            // Single use is the whole mechanism — this throw is what the service turns into
            // reuse detection.
            Assert.That(() => entity.Consume(Now.AddHours(2)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void Consume_WhenRevoked_ThrowDomainException()
        {
            var entity = CreateToken();
            entity.Revoke(Now.AddHours(1));

            Assert.That(() => entity.Consume(Now.AddHours(2)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void Revoke_AppliesToConsumedTokensAndIsIdempotent()
        {
            var entity = CreateToken();
            entity.Consume(Now.AddHours(1));

            // Revoking a lineage has to close the spent links too, so a consumed token is still a
            // valid target.
            entity.Revoke(Now.AddHours(2));

            Assert.That(entity.Status, Is.EqualTo(RefreshTokenStatus.Revoked));
            Assert.That(entity.RevokedAt, Is.EqualTo(Now.AddHours(2)));

            // Second call leaves the original timestamp alone: revocation happened once.
            entity.Revoke(Now.AddHours(3));

            Assert.That(entity.RevokedAt, Is.EqualTo(Now.AddHours(2)));
        }
    }
}
