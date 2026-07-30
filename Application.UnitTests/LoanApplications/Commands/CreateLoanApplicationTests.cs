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
    public class CreateLoanApplicationTests
    {
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<ILoanApplicationRepository> _applications;
        private Mock<IUnitOfWork> _unitOfWork;

        [SetUp]
        public void SetUp()
        {
            // One repository, mocked through its interface. The handler declares what it needs,
            // so there is nothing here to stub for repositories it never touches. No IDateTime
            // either — AuditableEntityInterceptor owns the timestamps now.
            _currentUserService = new Mock<ICurrentUserService>();
            _applications = new Mock<ILoanApplicationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();

            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _applications
                .Setup(r => r.AddAsync(It.IsAny<LoanApplication>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _unitOfWork.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        [Test]
        public async Task CreateApplicationCommand_WhenCalled_ReturnEntityId()
        {
            // Arrange
            var command = new CreateApplicationCommand()
            {
                Amount = 1,
                CurrencyId = 1,
                LoanTypeId = 1,
                PeriodPerMonth = 1
            };

            var handler = new CreateApplicationCommandHandler(
                _applications.Object,
                _unitOfWork.Object,
                _currentUserService.Object);

            // Act
            var result = await handler.Handle(command, default);

            // Assert

            // Ensure that result is int type
            Assert.That(result, Is.TypeOf<int>());
            // Ensure that repository AddAsync Method is called
            _applications.Verify(r => r.AddAsync(It.IsAny<LoanApplication>(), It.IsAny<CancellationToken>()));
            // Ensure that unit of work SaveChangesAsync Method is called
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        /// <summary>
        /// The InvalidUser invariant used to sit in <c>LoanApplication.Create</c>, which could
        /// enforce it because it was handed a user id. The entity no longer receives one, so this
        /// is where the rule lives — and nothing must be written when it fails.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CreateApplicationCommand_WithNoAuthenticatedUser_ThrowsAndPersistsNothing(string? userId)
        {
            _currentUserService.Setup(u => u.UserId).Returns(userId);

            var handler = new CreateApplicationCommandHandler(
                _applications.Object,
                _unitOfWork.Object,
                _currentUserService.Object);

            var command = new CreateApplicationCommand
            {
                Amount = 1,
                CurrencyId = 1,
                LoanTypeId = 1,
                PeriodPerMonth = 1
            };

            Assert.That(
                async () => await handler.Handle(command, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidUser"));

            _applications.Verify(
                r => r.AddAsync(It.IsAny<LoanApplication>(), It.IsAny<CancellationToken>()),
                Times.Never());
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
