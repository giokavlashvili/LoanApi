using Application.Common.Confirmation;
using Application.Common.Interfaces;
using Application.Confirmation.Commands;
using Application.Confirmation.Services;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

namespace Application.UnitTests.Confirmation
{
    [TestFixture]
    public class ConfirmOperationCommandHandlerTests
    {
        public record CapturedCommand : IRequest, IRequireOperationConfirmation
        {
            public int Id { get; set; }
        }

        private const string Discriminator = "test.op.v1";
        private const string Initiator = "initiator-id";
        private const string Approver = "approver-id";
        private const string Payload = """{"Id":42}""";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        private Mock<IPendingOperationRepository> _operations;
        private Mock<IOperationTypeRegistry> _registry;
        private Mock<IOtpService> _otpService;
        private Mock<IUnitOfWork> _unitOfWork;
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<ISender> _sender;
        private OperationExecutionContext _executionContext;
        private OperationConfirmationService _confirmationService;

        /// <summary>Challenge id → the request hash it was issued against.</summary>
        private Dictionary<Guid, string> _issuedChallenges;

        [SetUp]
        public void SetUp()
        {
            _operations = new Mock<IPendingOperationRepository>();
            _registry = new Mock<IOperationTypeRegistry>();
            _otpService = new Mock<IOtpService>();
            _unitOfWork = new Mock<IUnitOfWork>();
            _currentUserService = new Mock<ICurrentUserService>();
            _sender = new Mock<ISender>();
            _executionContext = new OperationExecutionContext();
            _issuedChallenges = new Dictionary<Guid, string>();

            var codeHasher = new Mock<IOtpCodeHasher>();
            codeHasher
                .Setup(h => h.Hash(It.IsAny<Guid>(), It.IsAny<string>()))
                .Returns((Guid id, string value) => $"hash:{id:N}:{value}");

            _confirmationService = new OperationConfirmationService(
                _otpService.Object,
                codeHasher.Object,
                _currentUserService.Object,
                Mock.Of<IUserService>());

            _currentUserService.Setup(c => c.UserId).Returns(Approver);

            _registry
                .Setup(r => r.Find(Discriminator))
                .Returns(new OperationTypeDescriptor(Discriminator, typeof(CapturedCommand), ConfirmationPolicy.DifferentUser, null));

            // Stands in for the real verification: a challenge only redeems the request hash it
            // was issued against, which is what OtpVerification.Verify enforces on RequestHash.
            _otpService
                .Setup(s => s.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((Guid challengeId, string purpose, string code, string requestHash, CancellationToken _) =>
                {
                    if (!_issuedChallenges.TryGetValue(challengeId, out var issuedFor))
                        throw new DomainValidationException("OtpChallengeNotFound");

                    if (!string.Equals(issuedFor, requestHash, StringComparison.Ordinal))
                        throw new DomainValidationException("OtpRequestMismatch");

                    return Task.CompletedTask;
                });

            _sender
                .Setup(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);
        }

        private PendingOperation GivenParkedOperation(string initiatedBy = Initiator)
        {
            var operation = PendingOperation.Create(Guid.NewGuid(), Discriminator, Payload, initiatedBy, Now.AddHours(24), Now);

            _operations
                .Setup(o => o.GetByOperationIdAsync(operation.OperationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation);

            return operation;
        }

        private Guid GivenChallengeFor(PendingOperation operation)
        {
            var challengeId = Guid.NewGuid();
            _issuedChallenges[challengeId] = _confirmationService.ComputeRequestHash(operation.OperationId, Discriminator);
            return challengeId;
        }

        private ConfirmOperationCommandHandler Handler() =>
            new(
                _operations.Object,
                _registry.Object,
                _confirmationService,
                _executionContext,
                _otpService.Object,
                _unitOfWork.Object,
                _currentUserService.Object,
                Mock.Of<IDateTime>(d => d.UtcNow == Now),
                _sender.Object);

        [Test]
        public async Task Confirm_WithAGoodCode_ExecuteTheCapturedCommand()
        {
            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            await Handler().Handle(
                new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Confirmed));
                Assert.That(operation.ConfirmedBy, Is.EqualTo(Approver));
            });

            _sender.Verify(s => s.Send(It.Is<object>(c => c is CapturedCommand && ((CapturedCommand)c).Id == 42), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The test that makes carrying <c>ChallengeId</c> on the wire safe. A code is bound to one
        /// operation through the request hash, not through the secrecy of the challenge id, so a
        /// challenge issued for one operation cannot redeem another.
        /// </summary>
        [Test]
        public void Confirm_WithAChallengeIssuedForAnotherOperation_Refuse()
        {
            var target = GivenParkedOperation();
            var other = GivenParkedOperation();
            var challengeForOther = GivenChallengeFor(other);

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = target.OperationId, ChallengeId = challengeForOther, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(target.Status, Is.EqualTo(PendingOperationStatus.Pending));
            _sender.Verify(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void Confirm_ByTheInitiatorUnderFourEyes_Refuse()
        {
            var operation = GivenParkedOperation(initiatedBy: Approver);
            var challengeId = GivenChallengeFor(operation);

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            _sender.Verify(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void Confirm_ByAThirdPartyUnderSameUser_Refuse()
        {
            _registry
                .Setup(r => r.Find(Discriminator))
                .Returns(new OperationTypeDescriptor(Discriminator, typeof(CapturedCommand), ConfirmationPolicy.SameUser, null));

            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void Confirm_AnUnknownOperation_Refuse()
        {
            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = Guid.NewGuid(), ChallengeId = Guid.NewGuid(), OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// Fail closed when the payload was parked under a discriminator that no longer exists —
        /// the alternative is deserializing into whatever the type looks like now.
        /// </summary>
        [Test]
        public void Confirm_AnOperationWhoseTypeIsGone_Refuse()
        {
            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            _registry.Setup(r => r.Find(Discriminator)).Returns((OperationTypeDescriptor?)null);

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            _sender.Verify(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Confirm_Twice_ExecuteOnlyOnce()
        {
            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            await Handler().Handle(
                new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                CancellationToken.None);

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            _sender.Verify(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void Confirm_AnExpiredOperation_RefuseEvenWithAGoodCode()
        {
            var operation = PendingOperation.Create(Guid.NewGuid(), Discriminator, Payload, Initiator, Now.AddMinutes(1), Now);

            _operations
                .Setup(o => o.GetByOperationIdAsync(operation.OperationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation);

            var challengeId = GivenChallengeFor(operation);

            var handler = new ConfirmOperationCommandHandler(
                _operations.Object, _registry.Object, _confirmationService, _executionContext, _otpService.Object,
                _unitOfWork.Object, _currentUserService.Object, Mock.Of<IDateTime>(d => d.UtcNow == Now.AddHours(2)), _sender.Object);

            Assert.That(async () => await handler.Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            _sender.Verify(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Pins the documented two-transaction behaviour. Confirmation commits before execution, so
        /// a failed command leaves the operation spent rather than replayable. That is deliberate —
        /// it is the direction that never executes twice — and it must not be "fixed" into a retry
        /// without transaction support the repo does not have.
        /// </summary>
        [Test]
        public void Confirm_WhenTheCapturedCommandFails_LeaveTheOperationSpent()
        {
            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            _sender
                .Setup(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainValidationException("ApplicationAlreadyProcessed"));

            Assert.That(async () => await Handler().Handle(
                    new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                    CancellationToken.None),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Confirmed));
        }

        /// <summary>
        /// The replay must run inside a confirmed-execution scope, or the behaviour would park the
        /// captured command a second time and the operation could never complete.
        /// </summary>
        [Test]
        public async Task Confirm_WhileDispatching_MarkTheExecutionAsConfirmed()
        {
            var operation = GivenParkedOperation();
            var challengeId = GivenChallengeFor(operation);

            bool? confirmedDuringDispatch = null;

            _sender
                .Setup(s => s.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null)
                .Callback(() => confirmedDuringDispatch = _executionContext.IsConfirmedExecution);

            await Handler().Handle(
                new ConfirmOperationCommand { OperationId = operation.OperationId, ChallengeId = challengeId, OtpCode = "123456" },
                CancellationToken.None);

            Assert.That(confirmedDuringDispatch, Is.True);
            // And restored afterwards, so nothing later in the same scope is ungated.
            Assert.That(_executionContext.IsConfirmedExecution, Is.False);
        }
    }
}
