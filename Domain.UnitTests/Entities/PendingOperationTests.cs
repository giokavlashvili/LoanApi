using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class PendingOperationTests
    {
        private const string OperationType = "ApproveLoan";
        private const string Payload = """{"loanId":42}""";

        private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        private static PendingOperation CreateOperation() =>
            PendingOperation.Create(Guid.NewGuid(), Guid.NewGuid(), OperationType, Payload, "user-id", Now);

        [Test]
        public void Create_StartsPendingAndKeepsThePayload()
        {
            var operation = CreateOperation();

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Pending));
            Assert.That(operation.Payload, Is.EqualTo(Payload));
            Assert.That(operation.ExecutedAt, Is.Null);
        }

        [Test]
        public void Create_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => PendingOperation.Create(Guid.Empty, Guid.NewGuid(), OperationType, Payload, null, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), Guid.Empty, OperationType, Payload, null, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), Guid.NewGuid(), string.Empty, Payload, null, Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), Guid.NewGuid(), OperationType, string.Empty, null, Now), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// A resend issues a new challenge and retires the old one. Without moving the operation
        /// onto it, every later confirm verifies against an invalidated challenge and fails
        /// permanently with OtpAlreadyUsed.
        /// </summary>
        [Test]
        public void Rechallenge_PointsAtTheNewChallenge()
        {
            var operation = CreateOperation();
            var reissued = Guid.NewGuid();

            operation.Rechallenge(reissued, Now.AddSeconds(90));

            Assert.That(operation.ChallengeId, Is.EqualTo(reissued));
            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Pending));
        }

        [Test]
        public void Rechallenge_WithAnEmptyIdOrAfterCompletion_ThrowDomainException()
        {
            var operation = CreateOperation();

            Assert.That(() => operation.Rechallenge(Guid.Empty, Now), Throws.InstanceOf<DomainValidationException>());

            operation.Succeed("{}", Now);

            Assert.That(() => operation.Rechallenge(Guid.NewGuid(), Now), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// The stored result is what makes a retry after a lost response replay rather than
        /// re-execute; the payload is dropped because it has served its purpose and the row is
        /// still wanted for audit.
        /// </summary>
        [Test]
        public void Succeed_StoresTheResultAndDropsThePayload()
        {
            var operation = CreateOperation();

            operation.Succeed("""{"approved":true}""", Now);

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Succeeded));
            Assert.That(operation.ResultPayload, Is.EqualTo("""{"approved":true}"""));
            Assert.That(operation.Payload, Is.Null);
            Assert.That(operation.ExecutedAt, Is.EqualTo(Now));
        }

        [Test]
        public void Fail_StoresTheErrorKeyAndDropsThePayload()
        {
            var operation = CreateOperation();

            operation.Fail("PendingOperationUnavailable", Now);

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Failed));
            Assert.That(operation.ErrorKey, Is.EqualTo("PendingOperationUnavailable"));
            Assert.That(operation.Payload, Is.Null);
        }

        [Test]
        public void Fail_WithoutAnErrorKey_ThrowDomainException()
        {
            Assert.That(() => CreateOperation().Fail(string.Empty, Now), Throws.InstanceOf<DomainValidationException>());
        }

        /// <summary>
        /// Terminal is terminal. Confirm dispatches on status before it touches the code, so a
        /// second transition would mean an operation running twice.
        /// </summary>
        [Test]
        public void SucceedOrFail_AfterCompletion_ThrowDomainException()
        {
            var succeeded = CreateOperation();
            succeeded.Succeed("{}", Now);

            Assert.That(() => succeeded.Succeed("{}", Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => succeeded.Fail("PendingOperationUnavailable", Now), Throws.InstanceOf<DomainValidationException>());

            var failed = CreateOperation();
            failed.Fail("PendingOperationUnavailable", Now);

            Assert.That(() => failed.Succeed("{}", Now), Throws.InstanceOf<DomainValidationException>());
        }
    }
}
