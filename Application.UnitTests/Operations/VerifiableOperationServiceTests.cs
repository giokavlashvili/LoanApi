using Application.Common.Interfaces;
using Application.Common.Operations;
using Application.Common.Otp;
using Application.Common.Serialization;
using Application.Operations.Services;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using FluentValidation;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using System.Text.Json;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.Operations
{
    [TestFixture]
    public class VerifiableOperationServiceTests
    {
        private const VerifiableOperationType Operation = VerifiableOperationType.DeleteLoanApplication;
        private const string OperationName = nameof(VerifiableOperationType.DeleteLoanApplication);
        private const string UserId = "user-id";
        private const string Recipient = "+995555123456";

        private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        public record ApproveLoanPayload
        {
            public int LoanId { get; init; }
        }

        private Mock<IVerifiableOperationRegistry> _registry;
        private Mock<IPendingOperationRepository> _operations;
        private Mock<IOtpService> _otpService;
        private Mock<IOtpCodeHasher> _codeHasher;
        private Mock<IOtpRecipientResolver> _recipientResolver;
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IIdentityService> _identityService;
        private Mock<IApplicationDbContext> _dbContext;
        private Mock<IUnitOfWork> _unitOfWork;
        private Mock<IDateTime> _dateTime;
        private ServiceCollection _serviceCollection;

        /// <summary>What the descriptor's Execute delegate returned, and whether it ran at all.</summary>
        private int _executeCount;
        private object? _executeResult;
        private Exception? _executeThrows;

        private bool _allowsCallerSuppliedRecipient;

        [SetUp]
        public void SetUp()
        {
            _executeCount = 0;
            _executeResult = new { approved = true };
            _executeThrows = null;
            _allowsCallerSuppliedRecipient = false;

            _registry = new Mock<IVerifiableOperationRegistry>();
            _operations = new Mock<IPendingOperationRepository>();
            _otpService = new Mock<IOtpService>();
            _codeHasher = new Mock<IOtpCodeHasher>();
            _recipientResolver = new Mock<IOtpRecipientResolver>();
            _currentUserService = new Mock<ICurrentUserService>();
            _identityService = new Mock<IIdentityService>();
            _dbContext = new Mock<IApplicationDbContext>();
            _unitOfWork = new Mock<IUnitOfWork>();
            _dateTime = new Mock<IDateTime>();
            _serviceCollection = new ServiceCollection();

            _dateTime.Setup(d => d.UtcNow).Returns(Now);
            _currentUserService.Setup(u => u.UserId).Returns(UserId);
            _recipientResolver.Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Recipient);
            _codeHasher.Setup(h => h.HashRequest(It.IsAny<object>())).Returns("payload-hash");
            _identityService.Setup(i => i.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            _dbContext.Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Mock<IDbContextTransaction>().Object);

            _otpService
                .Setup(s => s.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VerificationChannel>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OtpChallengeDto { ChallengeId = Guid.NewGuid(), ExpiresAt = Now.AddMinutes(5), MaxAttempts = 5 });

            // Both overloads: initiate addresses the registry by enum, confirm by the name it read
            // back off the stored row.
            _registry.Setup(r => r.Get(Operation)).Returns(() => Descriptor());
            _registry.Setup(r => r.Get(It.Is<VerifiableOperationType>(t => t != Operation)))
                .Throws(() => new DomainValidationException("UnknownVerifiableOperation"));
            _registry.Setup(r => r.Get(OperationName)).Returns(() => Descriptor());
            _registry.Setup(r => r.Get(It.Is<string>(n => n != OperationName)))
                .Throws(() => new DomainValidationException("UnknownVerifiableOperation"));
        }

        private VerifiableOperationDescriptor Descriptor() => new()
        {
            Type = Operation,
            PayloadType = typeof(ApproveLoanPayload),
            RequiresAuthentication = true,
            AllowsCallerSuppliedRecipient = _allowsCallerSuppliedRecipient,
            RequiredPolicies = [],
            Execute = (_, _, _) =>
            {
                _executeCount++;

                if (_executeThrows is not null)
                    throw _executeThrows;

                return Task.FromResult(_executeResult);
            }
        };

        private VerifiableOperationService CreateService() => new(
            _registry.Object,
            _operations.Object,
            _otpService.Object,
            _codeHasher.Object,
            _recipientResolver.Object,
            new ProtectedPayloadSerializer(new PassThroughProtector()),
            _currentUserService.Object,
            _identityService.Object,
            _dbContext.Object,
            _unitOfWork.Object,
            _dateTime.Object,
            _serviceCollection.BuildServiceProvider(),
            NullLogger<VerifiableOperationService>.Instance);

        private static JsonElement Payload(int loanId = 42) =>
            JsonSerializer.SerializeToElement(new ApproveLoanPayload { LoanId = loanId });

        private PendingOperation StoredOperation(Guid operationId, string? userId = UserId) =>
            PendingOperation.Create(operationId, Guid.NewGuid(), OperationName, """{"loanId":42}""", userId, Now);

        private void VerifyNothingWasSent() =>
            _otpService.Verify(
                s => s.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VerificationChannel>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never());

        // ------------------------------------------------------------------ initiate

        [Test]
        public async Task InitiateAsync_StoresTheOperationAndIssuesAChallenge()
        {
            var result = await CreateService().InitiateAsync(Operation, VerificationChannel.Sms, Payload());

            Assert.That(result.OperationId, Is.Not.EqualTo(Guid.Empty));
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Once());
            _otpService.Verify(
                s => s.IssueAsync(OperationName, Recipient, VerificationChannel.Sms, UserId, "payload-hash", It.IsAny<CancellationToken>()),
                Times.Once());
        }

        /// <summary>
        /// An out-of-range value, which is what an enum lets a caller send instead of an unknown
        /// name. The validator's <c>IsInEnum</c> would normally catch it first — the service must
        /// refuse it anyway, since the validator is not the thing standing between a caller and an
        /// SMS bill.
        /// </summary>
        [Test]
        public void InitiateAsync_WithAnUnregisteredOperation_ThrowsBeforeSending()
        {
            Assert.That(
                async () => await CreateService().InitiateAsync((VerifiableOperationType)999, VerificationChannel.Sms, Payload()),
                Throws.InstanceOf<DomainValidationException>());

            VerifyNothingWasSent();
        }

        /// <summary>
        /// Rejected, not ignored — a caller who believes they redirected the code and silently did
        /// not is worse off than one who got an error.
        /// </summary>
        [Test]
        public void InitiateAsync_WithARecipientTheOperationDoesNotAllow_ThrowsBeforeSending()
        {
            Assert.That(
                async () => await CreateService().InitiateAsync(Operation, VerificationChannel.Sms, Payload(), "+995555000000"),
                Throws.InstanceOf<DomainValidationException>());

            VerifyNothingWasSent();
        }

        [Test]
        public async Task InitiateAsync_WhenTheOperationAllowsIt_HonoursTheSuppliedRecipient()
        {
            _allowsCallerSuppliedRecipient = true;

            await CreateService().InitiateAsync(Operation, VerificationChannel.Sms, Payload(), "+995555000000");

            _recipientResolver.Verify(r => r.ResolveAsync("+995555000000", It.IsAny<CancellationToken>()), Times.Once());
        }

        /// <summary>
        /// The payload is frozen once stored, so a rejection left until confirm costs a message
        /// <em>and</em> a code and still needs a whole new challenge.
        /// </summary>
        [Test]
        public void InitiateAsync_WhenThePayloadFailsValidation_CostsNoMessage()
        {
            _serviceCollection.AddTransient<IValidator<ApproveLoanPayload>, AlwaysFailsValidator>();

            Assert.That(
                async () => await CreateService().InitiateAsync(Operation, VerificationChannel.Sms, Payload()),
                Throws.InstanceOf<Application.Common.Exceptions.ValidationException>());

            VerifyNothingWasSent();
            _operations.Verify(o => o.AddAsync(It.IsAny<PendingOperation>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void InitiateAsync_WithAnUnbindablePayload_ThrowsBeforeSending()
        {
            var wrongShape = JsonSerializer.SerializeToElement(new { loanId = "not-a-number" });

            Assert.That(
                async () => await CreateService().InitiateAsync(Operation, VerificationChannel.Sms, wrongShape),
                Throws.InstanceOf<DomainValidationException>());

            VerifyNothingWasSent();
        }

        // ------------------------------------------------------------------ confirm

        [Test]
        public async Task ConfirmAsync_VerifiesTheCodeThenExecutesAndCommits()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);

            var result = await CreateService().ConfirmAsync(operationId, "123456");

            Assert.That(_executeCount, Is.EqualTo(1));
            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(result.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            _dbContext.Verify(c => c.CommitTransactionAsync(It.IsAny<IDbContextTransaction>(), It.IsAny<CancellationToken>()), Times.Once());
            _dbContext.Verify(c => c.RollbackTransaction(), Times.Never());
        }

        /// <summary>
        /// A confirm whose response was lost must replay, not re-run and not report
        /// <c>OtpAlreadyUsed</c> — the client otherwise cannot tell whether the operation happened,
        /// which for anything moving money is the worst available answer.
        /// </summary>
        [Test]
        public async Task ConfirmAsync_AfterItAlreadySucceeded_ReplaysWithoutReExecuting()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            operation.Succeed("""{"approved":true}""", Now);
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);

            var result = await CreateService().ConfirmAsync(operationId, "123456");

            Assert.That(_executeCount, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(result.Result!.Value.GetProperty("approved").GetBoolean(), Is.True);

            // The status is inspected before the code is ever looked at, so a replay does not
            // spend an attempt either.
            _otpService.Verify(
                s => s.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Test]
        public void ConfirmAsync_AfterItFailed_ThrowsWithoutReExecuting()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            operation.Fail("PendingOperationUnavailable", Now);
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);

            Assert.That(async () => await CreateService().ConfirmAsync(operationId, "123456"),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(_executeCount, Is.Zero);
        }

        /// <summary>
        /// The same error as an unknown id, deliberately — a distinct "not yours" would confirm
        /// the operation exists.
        /// </summary>
        [Test]
        public void ConfirmAsync_ByADifferentPrincipal_ThrowsNotFoundAndDoesNotExecute()
        {
            var operationId = Guid.NewGuid();
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StoredOperation(operationId, "someone-else"));

            Assert.That(async () => await CreateService().ConfirmAsync(operationId, "123456"),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(_executeCount, Is.Zero);
            _otpService.Verify(
                s => s.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        /// <summary>
        /// The code is checked before the transaction opens, so a wrong code cannot have its
        /// AttemptCount increment rolled back — which is what keeps six digits from being
        /// brute-forceable.
        /// </summary>
        [Test]
        public void ConfirmAsync_WithAWrongCode_NeverOpensATransactionOrExecutes()
        {
            var operationId = Guid.NewGuid();
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StoredOperation(operationId));
            _otpService
                .Setup(s => s.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainValidationException("InvalidOtpCode"));

            Assert.That(async () => await CreateService().ConfirmAsync(operationId, "000000"),
                Throws.InstanceOf<DomainValidationException>());

            Assert.That(_executeCount, Is.Zero);
            _dbContext.Verify(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void ConfirmAsync_WhenTheHandlerThrows_RollsBackAndLeavesItPending()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);
            _executeThrows = new InvalidOperationException("handler failed");

            Assert.That(async () => await CreateService().ConfirmAsync(operationId, "123456"),
                Throws.InstanceOf<InvalidOperationException>());

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Pending));
            _dbContext.Verify(c => c.RollbackTransaction(), Times.Once());
            _dbContext.Verify(c => c.CommitTransactionAsync(It.IsAny<IDbContextTransaction>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        // ------------------------------------------------------------------ resend

        /// <summary>
        /// ResendAsync mints a new ChallengeId and retires the old one. Without moving the
        /// operation onto it, every later confirm verifies against an invalidated challenge and
        /// fails with OtpAlreadyUsed, permanently.
        /// </summary>
        [Test]
        public async Task ResendAsync_MovesTheOperationOntoTheNewChallenge()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            var original = operation.ChallengeId;
            var reissued = Guid.NewGuid();

            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);
            _otpService.Setup(s => s.ResendAsync(original, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OtpChallengeDto { ChallengeId = reissued, ExpiresAt = Now.AddMinutes(5) });

            var result = await CreateService().ResendAsync(operationId);

            Assert.That(operation.ChallengeId, Is.EqualTo(reissued));
            Assert.That(result.ChallengeId, Is.EqualTo(reissued));
            Assert.That(result.OperationId, Is.EqualTo(operationId), "the operation handle must not change");
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Test]
        public void ResendAsync_ForACompletedOperation_ThrowDomainException()
        {
            var operationId = Guid.NewGuid();
            var operation = StoredOperation(operationId);
            operation.Succeed("{}", Now);
            _operations.Setup(o => o.GetByOperationIdAsync(operationId, It.IsAny<CancellationToken>())).ReturnsAsync(operation);

            Assert.That(async () => await CreateService().ResendAsync(operationId), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// These tests are about the flow, not the cipher — <c>ApproveLoanPayload</c> carries no
        /// <c>[SensitiveData]</c>, so this is never actually called. It exists so the serializer can
        /// be constructed. Real encryption is exercised end to end in
        /// <c>VerifiedOperationFlowTests</c>, against a real database row.
        /// </summary>
        private sealed class PassThroughProtector : IPayloadProtector
        {
            public bool IsConfigured => true;
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string protectedValue) => protectedValue;
        }

        private class AlwaysFailsValidator : AbstractValidator<ApproveLoanPayload>
        {
            public AlwaysFailsValidator()
            {
                RuleFor(p => p.LoanId).Must(_ => false).WithMessage("nope");
            }
        }
    }
}
