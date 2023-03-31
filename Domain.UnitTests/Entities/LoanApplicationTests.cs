using Domain.Entities;
using Domain.Enums;
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
            Assert.That(() => LoanApplication.Create(1, 0, 1, 1,"userId", DateTime.Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => LoanApplication.Create(1, 1, 1, 0, "userId", DateTime.Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => LoanApplication.Create(1, 1, 1, 1,string.Empty, DateTime.Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplication_WithInvalidParameters_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", DateTime.Now);

            Assert.That(() => entity.Update(1, 0, 1, 1, "userId", DateTime.Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => entity.Update(1, 1, 1, 0, "userId", DateTime.Now), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => entity.Update(1, 1, 1, 1, string.Empty, DateTime.Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WithInvalidParameters_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", DateTime.Now);

            Assert.That(() => entity.UpdateStatus(LoanStatus.Sent, string.Empty, DateTime.Now), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WhenCalled_ChangeStatus()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", DateTime.Now);

            entity.UpdateStatus(LoanStatus.InProcess, "userId", DateTime.Now);

            Assert.That(entity.Status, Is.EqualTo(LoanStatus.InProcess));
        }
    }
}
