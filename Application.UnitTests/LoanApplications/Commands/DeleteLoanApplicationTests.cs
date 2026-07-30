using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Repositories;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.LoanApplications.Commands
{
    [TestFixture]
    public class DeleteLoanApplicationTests
    {
        private Mock<ILoanApplicationRepository> _applications;
        private Mock<IUnitOfWork> _unitOfWork;

        [SetUp]
        public void SetUp()
        {
            _applications = new Mock<ILoanApplicationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();

            _applications.Setup(r => r.Remove(It.IsAny<LoanApplication>()));
            _applications
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(LoanApplication.Create(1, 1, 1, 1, "UserId", new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc)));
            _unitOfWork.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        [Test]
        public async Task DeleteApplicationCommand_WhenCalled_DeleteApplication()
        {
            // Arrange
            var command = new DeleteApplicationCommand()
            {
                Id = 1
            };

            var handler = new DeleteApplicationCommandHandler(_applications.Object, _unitOfWork.Object);

            // Act
            await handler.Handle(command, default);

            // Assert
            // Ensure that repository Remove Method is called
            _applications.Verify(r => r.Remove(It.IsAny<LoanApplication>()));
            // Ensure that unit of work SaveChangesAsync Method is called
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }
}
