using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class PendingOperationTests
    {
        private const string OperationType = "loan.status.update.v1";
        private const string Payload = """{"id":1,"status":1}""";
        private const string Initiator = "initiator-id";

        private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0);

        private static PendingOperation CreateOperation() =>
            PendingOperation.Create(Guid.NewGuid(), OperationType, Payload, Initiator, Now.AddHours(24), Now);

        [Test]
        public void CreatePendingOperation_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => PendingOperation.Create(Guid.Empty, OperationType, Payload, Initiator, Now.AddHours(1), Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), string.Empty, Payload, Initiator, Now.AddHours(1), Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), OperationType, string.Empty, Initiator, Now.AddHours(1), Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), OperationType, Payload, string.Empty, Now.AddHours(1), Now), Throws.InstanceOf<DomainValidationException>());
            // Already expired when parked.
            Assert.That(() => PendingOperation.Create(Guid.NewGuid(), OperationType, Payload, Initiator, Now, Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void ConfirmOperation_ByAnyUser_MarkConfirmedAndRecordWho()
        {
            var operation = CreateOperation();

            operation.Confirm("approver-id", Now.AddMinutes(10));

            Assert.Multiple(() =>
            {
                Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Confirmed));
                Assert.That(operation.ConfirmedBy, Is.EqualTo("approver-id"));
                Assert.That(operation.ConfirmedAt, Is.EqualTo(Now.AddMinutes(10)));
            });
        }

        /// <summary>
        /// The state transition is the only thing stopping a captured command running twice, so
        /// this is the single most important assertion on the aggregate.
        /// </summary>
        [Test]
        public void ConfirmOperation_Twice_ThrowDomainException()
        {
            var operation = CreateOperation();

            operation.Confirm("approver-id", Now.AddMinutes(10));

            Assert.That(() => operation.Confirm("approver-id", Now.AddMinutes(11)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void ConfirmOperation_AfterExpiry_MarkExpiredAndThrow()
        {
            var operation = CreateOperation();

            Assert.That(() => operation.Confirm("approver-id", Now.AddHours(25)), Throws.InstanceOf<DomainValidationException>());
            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Expired));
        }

        [Test]
        public void ConfirmOperation_WithoutAConfirmer_ThrowDomainException()
        {
            var operation = CreateOperation();

            Assert.That(() => operation.Confirm(string.Empty, Now.AddMinutes(10)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void ConfirmOperation_AfterCancelling_ThrowDomainException()
        {
            var operation = CreateOperation();

            operation.Cancel(Now.AddMinutes(5));

            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Cancelled));
            Assert.That(() => operation.Confirm("approver-id", Now.AddMinutes(10)), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void CancelOperation_WhenAlreadyResolved_DoNothing()
        {
            var operation = CreateOperation();

            operation.Confirm("approver-id", Now.AddMinutes(10));
            operation.Cancel(Now.AddMinutes(11));

            // Cancelling twice, or cancelling something already confirmed, is not an error worth
            // surfacing — but it must not undo the resolution either.
            Assert.That(operation.Status, Is.EqualTo(PendingOperationStatus.Confirmed));
        }
    }
}
