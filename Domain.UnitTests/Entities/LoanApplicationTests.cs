using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using NUnit.Framework;

namespace Domain.UnitTests.Entities
{
    [TestFixture]
    public class LoanApplicationTests
    {
        private static readonly DateTime FixedUtcNow = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void CreateLoanApplication_WithInvalidParameters_ThrowDomainException()
        {
            Assert.That(() => LoanApplication.Create(1, 0, 1, 1,"userId", FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => LoanApplication.Create(1, 1, 1, 0, "userId", FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => LoanApplication.Create(1, 1, 1, 1,string.Empty, FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplication_WithInvalidParameters_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", FixedUtcNow);

            Assert.That(() => entity.Update(1, 0, 1, 1, "userId", FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => entity.Update(1, 1, 1, 0, "userId", FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
            Assert.That(() => entity.Update(1, 1, 1, 1, string.Empty, FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WithInvalidParameters_ThrowDomainException()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", FixedUtcNow);

            Assert.That(() => entity.UpdateStatus(LoanStatus.Sent, string.Empty, FixedUtcNow), Throws.InstanceOf<DomainValidationException>());
        }

        [Test]
        public void UpdateLoanApplicationStatus_WhenCalled_ChangeStatus()
        {
            var entity = LoanApplication.Create(1, 1, 1, 1, "userId", FixedUtcNow);

            entity.UpdateStatus(LoanStatus.InProcess, "userId", FixedUtcNow);

            Assert.That(entity.Status, Is.EqualTo(LoanStatus.InProcess));
        }
    }
}
