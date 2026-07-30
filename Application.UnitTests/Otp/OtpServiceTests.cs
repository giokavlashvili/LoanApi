using Application.Common.Interfaces;
using Application.Common.Models;
using Domain.Entities;
using Domain.Exceptions;
using Application.Otp.Services;
using Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Otp
{
    [TestFixture]
    public class OtpServiceTests
    {
        private const string Purpose = "GatedCommand";
        private const string Recipient = "+995555123456";
        // Valid Base64 so OtpVerification.Verify's constant-time comparison actually decodes it.
        private const string RequestHash = "cmVxdWVzdC1oYXNo";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        private Mock<IOtpVerificationRepository> _otpRepository;
        private Mock<IUnitOfWork> _unitOfWork;
        private Mock<IOtpCodeHasher> _codeHasher;
        private Mock<ISmsSender> _smsSender;
        private Mock<IDateTime> _dateTime;
        private OtpOptions _options;
        private OtpService _service;

        [SetUp]
        public void SetUp()
        {
            _otpRepository = new Mock<IOtpVerificationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();

            _codeHasher = new Mock<IOtpCodeHasher>();
            _smsSender = new Mock<ISmsSender>();
            _dateTime = new Mock<IDateTime>();
            _options = new OtpOptions { Secret = "test-secret", MaxAttempts = 3, ResendCooldown = TimeSpan.Zero };

            _dateTime.Setup(d => d.UtcNow).Returns(Now);
            _unitOfWork.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var optionsMonitor = new Mock<IOptionsMonitor<OtpOptions>>();
            optionsMonitor.Setup(m => m.CurrentValue).Returns(() => _options);

            _service = new OtpService(
                _otpRepository.Object,
                _unitOfWork.Object,
                _codeHasher.Object,
                _smsSender.Object,
                _dateTime.Object,
                optionsMonitor.Object,
                new Mock<ILogger<OtpService>>().Object);
        }

        private OtpVerification CreateChallenge(Guid challengeId) =>
            OtpVerification.Create(
                challengeId,
                Purpose,
                Recipient,
                userId: null,
                "Y29ycmVjdC1oYXNo",
                RequestHash,
                Now.AddMinutes(5),
                _options.MaxAttempts,
                Now);

        [Test]
        public async Task IssueAsync_WhenCalled_PersistChallengeThenSend()
        {
            // Arrange
            _codeHasher.Setup(h => h.Hash(It.IsAny<Guid>(), It.IsAny<string>())).Returns("hashed");

            // Act
            var result = await _service.IssueAsync(Purpose, Recipient, null, RequestHash);

            // Assert
            Assert.That(result.ChallengeId, Is.Not.EqualTo(Guid.Empty));
            // Masked so the response cannot disclose a number to someone who does not have it.
            Assert.That(result.Recipient, Is.EqualTo("*********3456"));
            _otpRepository.Verify(r => r.AddAsync(It.IsAny<OtpVerification>(), It.IsAny<CancellationToken>()), Times.Once());
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
            _smsSender.Verify(s => s.SendAsync(Recipient, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task IssueAsync_WithStaticCodeConfigured_SendTheFixedCodeAndAcceptIt()
        {
            // Arrange — the Development-only escape hatch: a fixed code instead of a random one.
            _options.StaticCode = "123456";
            _codeHasher.Setup(h => h.Hash(It.IsAny<Guid>(), "123456")).Returns("c3RhdGljLWhhc2g=");

            OtpVerification? stored = null;
            _otpRepository
                .Setup(r => r.AddAsync(It.IsAny<OtpVerification>(), It.IsAny<CancellationToken>()))
                .Callback<OtpVerification, CancellationToken>((e, _) => stored = e)
                .Returns(Task.CompletedTask);

            // Act
            var challenge = await _service.IssueAsync(Purpose, Recipient, null, RequestHash);

            // Assert — the fixed code was what got texted...
            _smsSender.Verify(s => s.SendAsync(Recipient, It.Is<string>(m => m.Contains("123456")), It.IsAny<CancellationToken>()), Times.Once());

            // ...and the same code the tester types back in is what verifies successfully. The
            // rest of the flow — expiry, attempt limit, purpose and payload binding — is untouched.
            Assert.That(stored, Is.Not.Null);
            _otpRepository.Setup(r => r.GetByChallengeIdAsync(challenge.ChallengeId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

            await _service.VerifyAsync(challenge.ChallengeId, Purpose, "123456", RequestHash);

            Assert.That(stored!.ConsumedAt, Is.Not.Null);
        }

        [Test]
        public void IssueAsync_PastHourlyCap_ThrowDomainException()
        {
            // Arrange
            _options.MaxPerRecipientPerHour = 3;
            _otpRepository
                .Setup(r => r.CountRecentAsync(Recipient, Purpose, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            // Act / Assert
            Assert.That(async () => await _service.IssueAsync(Purpose, Recipient, null, RequestHash), Throws.InstanceOf<DomainValidationException>());

            // Nothing is sent once the cap is hit, which is the point — the cap exists to stop
            // the endpoint being used as a free SMS relay.
            _smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void IssueAsync_WithinResendCooldown_ThrowDomainException()
        {
            // Arrange
            _options.ResendCooldown = TimeSpan.FromMinutes(1);
            _otpRepository
                .Setup(r => r.GetLatestAsync(Recipient, Purpose, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateChallenge(Guid.NewGuid()));

            // Act / Assert
            Assert.That(async () => await _service.IssueAsync(Purpose, Recipient, null, RequestHash), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void VerifyAsync_WithWrongCode_PersistTheFailedAttempt()
        {
            // Arrange
            var challengeId = Guid.NewGuid();
            var entity = CreateChallenge(challengeId);

            _otpRepository.Setup(r => r.GetByChallengeIdAsync(challengeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _codeHasher.Setup(h => h.Hash(challengeId, "000000")).Returns("d3JvbmctaGFzaA==");

            // Act
            Assert.That(async () => await _service.VerifyAsync(challengeId, Purpose, "000000", RequestHash), Throws.InstanceOf<DomainValidationException>());

            // Assert — this is the whole reason the save sits in a finally. Without it the
            // increment rolls back on every wrong guess, MaxAttempts is never reached, and a six
            // digit code can be walked at leisure.
            Assert.That(entity.AttemptCount, Is.EqualTo(1));
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task VerifyAsync_WithCorrectCode_ConsumeChallenge()
        {
            // Arrange
            var challengeId = Guid.NewGuid();
            var entity = CreateChallenge(challengeId);

            _otpRepository.Setup(r => r.GetByChallengeIdAsync(challengeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            _codeHasher.Setup(h => h.Hash(challengeId, "123456")).Returns("Y29ycmVjdC1oYXNo");

            // Act
            await _service.VerifyAsync(challengeId, Purpose, "123456", RequestHash);

            // Assert
            Assert.That(entity.ConsumedAt, Is.Not.Null);
            _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void VerifyAsync_ForADifferentPurpose_ThrowDomainException()
        {
            // Arrange
            var challengeId = Guid.NewGuid();

            _otpRepository.Setup(r => r.GetByChallengeIdAsync(challengeId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateChallenge(challengeId));
            _codeHasher.Setup(h => h.Hash(challengeId, "123456")).Returns("Y29ycmVjdC1oYXNo");

            // Act / Assert — a valid code for registration must not approve a loan.
            Assert.That(
                async () => await _service.VerifyAsync(challengeId, "SomeOtherCommand", "123456", RequestHash),
                Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void VerifyAsync_WithUnknownChallenge_ThrowDomainException()
        {
            // Arrange
            _otpRepository
                .Setup(r => r.GetByChallengeIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OtpVerification?)null);

            // Act / Assert
            Assert.That(
                async () => await _service.VerifyAsync(Guid.NewGuid(), Purpose, "123456", RequestHash),
                Throws.InstanceOf<DomainValidationException>());
        }
    }
}
