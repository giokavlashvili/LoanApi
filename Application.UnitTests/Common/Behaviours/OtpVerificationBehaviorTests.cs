using Application.Common.Behaviors;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Otp;
using Application.Otp.Dtos;
using Domain.Enums;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Common.Behaviours
{
    [TestFixture]
    public class OtpVerificationBehaviorTests
    {
        /// <summary>A command that opts in and supplies its own recipient, like registration.</summary>
        public class GatedCommand : IRequest<bool>, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; set; }
            public string? OtpCode { get; set; }
            public string? OtpRecipient => "+995555123456";
        }

        /// <summary>A command that opts in without one, like an authenticated business operation.</summary>
        public class GatedCommandWithoutRecipient : IRequest<bool>, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; set; }
            public string? OtpCode { get; set; }
        }

        public class PlainCommand : IRequest<bool>
        {
        }

        /// <summary>
        /// A void command, i.e. <c>IRequest</c> rather than <c>IRequest&lt;T&gt;</c>. Worth its
        /// own case: MediatR.Contracts 2.x made the two interfaces unrelated, so a behavior
        /// constrained to <c>TRequest : IRequest&lt;TResponse&gt;</c> cannot close over
        /// <c>&lt;this, Unit&gt;</c> and the container drops it without a word.
        /// </summary>
        public class GatedVoidCommand : IRequest, IRequireOtpVerification
        {
            public Guid? ChallengeId { get; set; }
            public string? OtpCode { get; set; }
            public string? OtpRecipient => "+995555123456";
        }

        private Mock<IOtpService> _otpService;
        private Mock<IOtpCodeHasher> _codeHasher;
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IOtpRecipientResolver> _recipientResolver;

        [SetUp]
        public void SetUp()
        {
            _otpService = new Mock<IOtpService>();
            _codeHasher = new Mock<IOtpCodeHasher>();
            _currentUserService = new Mock<ICurrentUserService>();
            _recipientResolver = new Mock<IOtpRecipientResolver>();

            // Stands in for the real rule, which OtpRecipientResolverTests covers: honour a
            // recipient the command supplied, otherwise fall back to the account's number.
            _recipientResolver
                .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? supplied, CancellationToken _) => supplied ?? "+995555999888");

            _codeHasher.Setup(h => h.HashRequest(It.IsAny<object>())).Returns("request-hash");

            _otpService
                .Setup(s => s.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VerificationChannel>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OtpChallengeDto { ChallengeId = Guid.NewGuid(), ExpiresAt = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc).AddMinutes(5) });
        }

        private OtpVerificationBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>() where TRequest : notnull =>
            new(
                _otpService.Object,
                _codeHasher.Object,
                _currentUserService.Object,
                _recipientResolver.Object,
                new Mock<ILogger<OtpVerificationBehavior<TRequest, TResponse>>>().Object);

        private OtpVerificationBehavior<TRequest, bool> CreateBehavior<TRequest>() where TRequest : notnull =>
            CreateBehavior<TRequest, bool>();

        [Test]
        public async Task Handle_WithRequestThatDoesNotOptIn_CallHandlerWithoutIssuingChallenge()
        {
            // Arrange
            var behavior = CreateBehavior<PlainCommand>();
            var handlerCalled = false;

            // Act
            var result = await behavior.Handle(new PlainCommand(), _ =>
            {
                handlerCalled = true;
                return Task.FromResult(true);
            }, default);

            // Assert
            Assert.That(handlerCalled, Is.True);
            Assert.That(result, Is.True);
            _otpService.Verify(s => s.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VerificationChannel>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void Handle_WithoutCode_IssueChallengeAndThrowWithoutCallingHandler()
        {
            // Arrange
            var behavior = CreateBehavior<GatedCommand>();
            var handlerCalled = false;

            // Act / Assert
            Assert.That(
                async () => await behavior.Handle(new GatedCommand(), _ =>
                {
                    handlerCalled = true;
                    return Task.FromResult(true);
                }, default),
                Throws.InstanceOf<OtpRequiredException>());

            // The operation must not run just because a code was requested for it.
            Assert.That(handlerCalled, Is.False);
            _otpService.Verify(s => s.IssueAsync("GatedCommand", "+995555123456", VerificationChannel.Sms, It.IsAny<string?>(), "request-hash", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public async Task Handle_WithCode_VerifyThenCallHandler()
        {
            // Arrange
            var behavior = CreateBehavior<GatedCommand>();
            var challengeId = Guid.NewGuid();
            var command = new GatedCommand { ChallengeId = challengeId, OtpCode = "123456" };
            var handlerCalled = false;

            // Act
            var result = await behavior.Handle(command, _ =>
            {
                handlerCalled = true;
                return Task.FromResult(true);
            }, default);

            // Assert
            Assert.That(handlerCalled, Is.True);
            Assert.That(result, Is.True);
            _otpService.Verify(s => s.VerifyAsync(challengeId, "GatedCommand", "123456", "request-hash", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void Handle_ForAVoidCommand_StillGate()
        {
            // Arrange — a command declared as IRequest, not IRequest<T>. If the behavior is ever
            // constrained back to "TRequest : IRequest<TResponse>" this stops compiling, which is
            // the point: the container gives no such warning at runtime, it just drops the
            // behavior and every void command silently loses its gate.
            var behavior = CreateBehavior<GatedVoidCommand, Unit>();

            // Act / Assert
            Assert.That(
                async () => await behavior.Handle(new GatedVoidCommand(), _ => Task.FromResult(Unit.Value), default),
                Throws.InstanceOf<OtpRequiredException>());

            _otpService.Verify(s => s.IssueAsync("GatedVoidCommand", "+995555123456", VerificationChannel.Sms, It.IsAny<string?>(), "request-hash", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void Handle_WhenTheRecipientCannotBeResolved_PropagateTheDomainException()
        {
            // Arrange — the resolver refuses, e.g. nobody authenticated to fall back to.
            var behavior = CreateBehavior<GatedCommandWithoutRecipient>();
            _recipientResolver
                .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainValidationException("OtpRecipientRequired"));

            // Act / Assert
            Assert.That(
                async () => await behavior.Handle(new GatedCommandWithoutRecipient(), _ => Task.FromResult(true), default),
                Throws.InstanceOf<DomainValidationException>());

            _otpService.Verify(
                s => s.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VerificationChannel>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        /// <summary>
        /// The behavior hands the command's own recipient to the resolver and issues against
        /// whatever comes back — it does not decide the number itself. The rule that a command
        /// without one falls back to the account is <c>OtpRecipientResolverTests</c>.
        /// </summary>
        [Test]
        public void Handle_WithoutRecipient_IssueAgainstTheResolvedNumber()
        {
            // Arrange
            var behavior = CreateBehavior<GatedCommandWithoutRecipient>();
            _currentUserService.Setup(u => u.UserId).Returns("userId");

            // Act / Assert
            Assert.That(
                async () => await behavior.Handle(new GatedCommandWithoutRecipient(), _ => Task.FromResult(true), default),
                Throws.InstanceOf<OtpRequiredException>());

            _recipientResolver.Verify(r => r.ResolveAsync(null, It.IsAny<CancellationToken>()), Times.Once());
            _otpService.Verify(s => s.IssueAsync("GatedCommandWithoutRecipient", "+995555999888", VerificationChannel.Sms, "userId", "request-hash", It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
