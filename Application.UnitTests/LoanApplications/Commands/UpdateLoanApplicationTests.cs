using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.LoanApplications.Commands
{
    [TestFixture]
    public class UpdateLoanApplicationTests
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

        [Test]
        public async Task UpdateApplicationCommand_WhenCalled_UpdateApplication()
        {
            // Arrange
            var command = new UpdateApplicationCommand()
            {
                Amount = 500,
                CurrencyId = 2,
                LoanTypeId = 3,
                PeriodPerMonth = 24,
                Id = 1
            };

            var handler = new UpdateApplicationCommandHandler(
                _applications.Object,
                _unitOfWork.Object,
                _currentUserService.Object);

            // Act
            await handler.Handle(command, default);

            // Assert — no repository.Update call: the aggregate loaded by GetByIdAsync is
            // already tracked, so mutating it in place is what actually persists the change.
            Assert.That(_entity.Amount, Is.EqualTo(command.Amount));
            Assert.That(_entity.CurrencyId, Is.EqualTo(command.CurrencyId));
            Assert.That(_entity.LoanTypeId, Is.EqualTo(command.LoanTypeId));
            Assert.That(_entity.PeriodPerMonth, Is.EqualTo(command.PeriodPerMonth));
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());

            // The handler no longer sets these; the interceptor does, at the save boundary, and
            // there is no interceptor in a mocked unit of work.
            Assert.That(_entity.LastModified, Is.Null);
            Assert.That(_entity.LastModifiedBy, Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void UpdateApplicationCommand_WithNoAuthenticatedUser_ThrowsAndPersistsNothing(string? userId)
        {
            _currentUserService.Setup(u => u.UserId).Returns(userId);

            var handler = new UpdateApplicationCommandHandler(
                _applications.Object,
                _unitOfWork.Object,
                _currentUserService.Object);

            var command = new UpdateApplicationCommand { Id = 1, Amount = 500, CurrencyId = 2, LoanTypeId = 3, PeriodPerMonth = 24 };

            Assert.That(
                async () => await handler.Handle(command, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidUser"));

            // Guarded before the aggregate is even loaded, so nothing was mutated.
            Assert.That(_entity.Amount, Is.EqualTo(100));
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
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
        public void UpdateApplicationCommand_WhenApplicationDisappearsAfterValidation_ThrowsAndPersistsNothing()
        {
            _applications
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LoanApplication?)null);

            var handler = new UpdateApplicationCommandHandler(
                _applications.Object,
                _unitOfWork.Object,
                _currentUserService.Object);

            var command = new UpdateApplicationCommand { Id = 1, Amount = 500, CurrencyId = 2, LoanTypeId = 3, PeriodPerMonth = 24 };

            Assert.That(
                async () => await handler.Handle(command, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidApplication"));

            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
