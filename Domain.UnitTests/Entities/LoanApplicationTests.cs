using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class LoanApplicationTests
    {
        [Test]
        public void CreateLoanApplication_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => LoanApplication.Create(1, 0, 1, 1), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => LoanApplication.Create(1, 1, 1, 0), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplication_WithInvalidParameters_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1);

            Assert.That(() => entity.Update(1, 0, 1, 1), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => entity.Update(1, 1, 1, 0), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WhenAlreadyProcessed_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1);

            entity.UpdateStatus(LoanStatus.Accepted);

            Assert.That(() => entity.UpdateStatus(LoanStatus.Rejected), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WhenCalled_ChangeStatus()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1);

            entity.UpdateStatus(LoanStatus.InProcess);

            Assert.That(entity.Status, Is.EqualTo(LoanStatus.InProcess));
        }

        /// <summary>
        /// The audit columns are the interceptor's job now, so the factory must leave them alone —
        /// if it stamped <c>Created</c> itself the interceptor would skip the entity, since it only
        /// fills in values the entity did not set.
        /// </summary>
        [Test]
        public void CreateLoanApplication_LeavesAuditFieldsUnset()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1);

            Assert.That(entity.Created, Is.EqualTo(default(DateTime)));
            Assert.That(entity.CreatedBy, Is.Null);
            Assert.That(entity.LastModified, Is.Null);
            Assert.That(entity.LastModifiedBy, Is.Null);
        }

        /// <summary>
        /// Deletion is raised by the aggregate, not by the command handler.
        /// </summary>
        [Test]
        public void Delete_RaisesApplicationDeletedEvent()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1);

            entity.Delete();

            Assert.That(entity.DomainEvents.Any(e => e is ApplicationDeletedEvent), Is.True);
        }
    }
}
