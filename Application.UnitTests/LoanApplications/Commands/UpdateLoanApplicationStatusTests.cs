using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Repositories;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.LoanApplications.Commands
{
    [TestFixture]
    public class UpdateLoanApplicationStatusTests
    {
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<ILoanApplicationRepository> _applications;
        private Mock<IUnitOfWork> _unitOfWork;
        private LoanApplication _entity;

        [SetUp]
        public void SetUp()
        {
            _currentUserService = new Mock<ICurrentUserService>();
            _applications = new Mock<ILoanApplicationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();

            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _entity = LoanApplication.Create(1, 100, 5, 6);
            _applications.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(_entity);
            _unitOfWork.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        private UpdateApplicationStatusCommandHandler CreateHandler() =>
            new(_applications.Object, _unitOfWork.Object, _currentUserService.Object);

        /// <summary>
        /// The OTP gate lives in <c>OtpVerificationBehavior</c>, not in the handler, so the handler
        /// under test here is the post-verification half only.
        /// </summary>
        [Test]
        public async Task UpdateApplicationStatusCommand_WhenCalled_UpdatesStatus()
        {
            var command = new UpdateApplicationStatusCommand { Id = 1, Status = LoanStatus.Accepted };

            await CreateHandler().Handle(command, default);

            // No repository.Update call: the aggregate loaded by GetByIdAsync is already tracked,
            // so mutating it in place is what actually persists the change.
            Assert.That(_entity.Status, Is.EqualTo(LoanStatus.Accepted));
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        /// <summary>
        /// The validator establishes the id exists, but it runs outside the transaction and before
        /// the handler's load — so a delete landing in that window returns null here. This used to
        /// dereference null and surface as a 500; it must be the validator's own key instead.
        /// <para>
        /// Do not "fix" this by restoring <c>#pragma warning disable CS8602</c> to the command file.
        /// The warning is what caught this.
        /// </para>
        /// </summary>
        [Test]
        public void UpdateApplicationStatusCommand_WhenApplicationDisappearsAfterValidation_ThrowsAndPersistsNothing()
        {
            _applications
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LoanApplication?)null);

            var command = new UpdateApplicationStatusCommand { Id = 1, Status = LoanStatus.Accepted };

            Assert.That(
                async () => await CreateHandler().Handle(command, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidApplication"));

            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void UpdateApplicationStatusCommand_WithNoAuthenticatedUser_ThrowsAndPersistsNothing(string? userId)
        {
            _currentUserService.Setup(u => u.UserId).Returns(userId);

            var command = new UpdateApplicationStatusCommand { Id = 1, Status = LoanStatus.Accepted };

            Assert.That(
                async () => await CreateHandler().Handle(command, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidUser"));

            // Guarded before the aggregate is even loaded, so nothing was mutated.
            Assert.That(_entity.Status, Is.EqualTo(LoanStatus.Sent));
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
