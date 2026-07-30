using Application.Authenticate.Services;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Authenticate
{
    [TestFixture]
    public class RefreshTokenServiceTests
    {
        private const string UserId = "9c1d0d5e-4b6f-4c2a-8a1b-6f0d2e3a4b5c";
        private const string PresentedToken = "presented-token";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        private Mock<IRefreshTokenRepository> _refreshTokens;
        private Mock<IUnitOfWork> _unitOfWork;
        private Mock<IRefreshTokenHasher> _hasher;
        private Mock<IDateTime> _dateTime;
        private Mock<ILogger<RefreshTokenService>> _logger;
        private RefreshTokenOptions _options;
        private RefreshTokenService _service;

        [SetUp]
        public void SetUp()
        {
            _refreshTokens = new Mock<IRefreshTokenRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();
            _hasher = new Mock<IRefreshTokenHasher>();
            _dateTime = new Mock<IDateTime>();
            _logger = new Mock<ILogger<RefreshTokenService>>();
            _options = new RefreshTokenOptions { Secret = new string('k', 32), ExpireDays = 14, AbsoluteExpireDays = 30 };

            _dateTime.Setup(d => d.UtcNow).Returns(Now);
            _unitOfWork.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            // The hash is only ever compared to itself here, so echoing the input is enough and
            // keeps the assertions about which token was looked up readable.
            _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(token => $"hash:{token}");

            var optionsMonitor = new Mock<IOptionsMonitor<RefreshTokenOptions>>();
            optionsMonitor.Setup(m => m.CurrentValue).Returns(() => _options);

            _service = new RefreshTokenService(
                _refreshTokens.Object,
                _unitOfWork.Object,
                _hasher.Object,
                _dateTime.Object,
                optionsMonitor.Object,
                _logger.Object);
        }

        private RefreshToken CreateStored(
            Guid sessionId,
            DateTime? expiresAt = null,
            DateTime? sessionExpiresAt = null) =>
            RefreshToken.Create(
                sessionId,
                UserId,
                $"hash:{PresentedToken}",
                expiresAt ?? Now.AddDays(_options.ExpireDays),
                sessionExpiresAt ?? Now.AddDays(_options.AbsoluteExpireDays),
                Now);

        [Test]
        public async Task IssueAsync_WhenCalled_StoresAHashedTokenAndReturnsTheRawOne()
        {
            RefreshToken? stored = null;
            _refreshTokens
                .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((e, _) => stored = e)
                .Returns(Task.CompletedTask);

            var issued = await _service.IssueAsync(UserId);

            Assert.That(issued.UserId, Is.EqualTo(UserId));
            Assert.That(issued.Token, Is.Not.Empty);
            Assert.That(stored, Is.Not.Null);

            // The raw token exists only in the response; the row holds a hash of it.
            Assert.That(stored!.TokenHash, Is.EqualTo($"hash:{issued.Token}"));
            Assert.That(stored.TokenHash, Is.Not.EqualTo(issued.Token));

            Assert.That(stored.Status, Is.EqualTo(RefreshTokenStatus.Active));
            Assert.That(stored.SessionExpiresAt, Is.EqualTo(Now.AddDays(30)));
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task IssueAsync_TwiceForOneUser_OpensSeparateSessions()
        {
            var sessions = new List<Guid>();
            _refreshTokens
                .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((e, _) => sessions.Add(e.SessionId))
                .Returns(Task.CompletedTask);

            await _service.IssueAsync(UserId);
            await _service.IssueAsync(UserId);

            // Two logins are two sessions: revoking one device must not sign the other one out.
            Assert.That(sessions, Has.Count.EqualTo(2));
            Assert.That(sessions[0], Is.Not.EqualTo(sessions[1]));
        }

        [Test]
        public async Task RotateAsync_WithAnActiveToken_ConsumesItAndInheritsTheSession()
        {
            var sessionId = Guid.NewGuid();
            var existing = CreateStored(sessionId);

            _refreshTokens
                .Setup(r => r.GetByHashAsync($"hash:{PresentedToken}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            RefreshToken? replacement = null;
            _refreshTokens
                .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((e, _) => replacement = e)
                .Returns(Task.CompletedTask);

            var rotated = await _service.RotateAsync(PresentedToken);

            Assert.That(existing.Status, Is.EqualTo(RefreshTokenStatus.Consumed));
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement!.SessionId, Is.EqualTo(sessionId));

            // The ceiling is inherited unchanged, which is what stops rotation renewing a session
            // forever.
            Assert.That(replacement.SessionExpiresAt, Is.EqualTo(existing.SessionExpiresAt));
            Assert.That(rotated.Token, Is.Not.EqualTo(PresentedToken));

            // One save for the consume and the insert together: a commit holding only one of them
            // would leave the lineage with two live tokens or none.
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task RotateAsync_NearTheSessionCeiling_ClampsTheReplacementToIt()
        {
            // Two days of session left, but ExpireDays is 14. The entity refuses to outlive its own
            // session, so the presented token is at the ceiling too.
            var existing = CreateStored(Guid.NewGuid(), expiresAt: Now.AddDays(2), sessionExpiresAt: Now.AddDays(2));

            _refreshTokens
                .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            RefreshToken? replacement = null;
            _refreshTokens
                .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((e, _) => replacement = e)
                .Returns(Task.CompletedTask);

            await _service.RotateAsync(PresentedToken);

            Assert.That(replacement!.ExpiresAt, Is.EqualTo(Now.AddDays(2)));
        }

        [Test]
        public void RotateAsync_WithAnUnknownToken_ThrowInvalidCredentials()
        {
            _refreshTokens
                .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RefreshToken?)null);

            Assert.That(async () => await _service.RotateAsync(PresentedToken), Throws.InstanceOf<InvalidCredentialsException>());

            _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void RotateAsync_WithAnExpiredToken_ThrowInvalidCredentialsWithoutRevoking()
        {
            // Issued valid — a token cannot be constructed already expired — and then overtaken by
            // the clock.
            var existing = CreateStored(Guid.NewGuid(), expiresAt: Now.AddDays(1), sessionExpiresAt: Now.AddDays(10));

            _dateTime.Setup(d => d.UtcNow).Returns(Now.AddDays(2));

            _refreshTokens
                .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            Assert.That(async () => await _service.RotateAsync(PresentedToken), Throws.InstanceOf<InvalidCredentialsException>());

            // Expiry is not a breach — the lineage is simply over, so nothing needs revoking.
            Assert.That(existing.Status, Is.EqualTo(RefreshTokenStatus.Active));
            _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void RotateAsync_WithAConsumedToken_RevokesTheWholeLineageAndCommitsBeforeThrowing()
        {
            var sessionId = Guid.NewGuid();

            var spent = CreateStored(sessionId);
            spent.Consume(Now);

            var successor = RefreshToken.Create(
                sessionId, UserId, "hash:successor", Now.AddDays(14), Now.AddDays(30), Now);

            _refreshTokens
                .Setup(r => r.GetByHashAsync($"hash:{PresentedToken}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(spent);

            _refreshTokens
                .Setup(r => r.ListBySessionAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RefreshToken> { spent, successor });

            Assert.That(async () => await _service.RotateAsync(PresentedToken), Throws.InstanceOf<InvalidCredentialsException>());

            // Reuse cannot be attributed to the thief or the victim, so both parties lose the
            // session — including the live successor the legitimate client may be holding.
            Assert.That(spent.Status, Is.EqualTo(RefreshTokenStatus.Revoked));
            Assert.That(successor.Status, Is.EqualTo(RefreshTokenStatus.Revoked));

            // Committed before the throw. An uncommitted revocation would leave the stolen lineage
            // live, which is the entire point of this branch.
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
            _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void RotateAsync_WithAConsumedToken_LogsTheStatusAsPresentedNotAsLeftBehind()
        {
            var sessionId = Guid.NewGuid();

            var spent = CreateStored(sessionId);
            spent.Consume(Now);

            _refreshTokens
                .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(spent);

            _refreshTokens
                .Setup(r => r.ListBySessionAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RefreshToken> { spent });

            Assert.That(async () => await _service.RotateAsync(PresentedToken), Throws.InstanceOf<InvalidCredentialsException>());

            // The warning has to say Consumed — a replayed token — and not Revoked, which is what
            // the revocation two lines earlier leaves behind. Reading the status after the revoke
            // reported "Revoked" for every case and made the log useless for telling a first replay
            // apart from continued attempts on a dead session.
            _logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(nameof(RefreshTokenStatus.Consumed))),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once());
        }

        [Test]
        public async Task RevokeSessionAsync_WithAKnownToken_RevokesEveryTokenInTheLineage()
        {
            var sessionId = Guid.NewGuid();

            var current = CreateStored(sessionId);
            var previous = RefreshToken.Create(
                sessionId, UserId, "hash:previous", Now.AddDays(14), Now.AddDays(30), Now);
            previous.Consume(Now);

            _refreshTokens
                .Setup(r => r.GetByHashAsync($"hash:{PresentedToken}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(current);

            _refreshTokens
                .Setup(r => r.ListBySessionAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RefreshToken> { previous, current });

            await _service.RevokeSessionAsync(PresentedToken);

            Assert.That(current.Status, Is.EqualTo(RefreshTokenStatus.Revoked));
            Assert.That(previous.Status, Is.EqualTo(RefreshTokenStatus.Revoked));
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task RevokeSessionAsync_WithAnUnknownToken_SucceedsSilently()
        {
            _refreshTokens
                .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RefreshToken?)null);

            // No throw: logout is not an authorization decision, and failing here would tell a
            // caller which tokens are real.
            await _service.RevokeSessionAsync(PresentedToken);

            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
