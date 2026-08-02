using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Events;
using Domain.Exceptions;
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
        private LoanApplication _entity;

        [SetUp]
        public void SetUp()
        {
            _applications = new Mock<ILoanApplicationRepository>();
            _unitOfWork = new Mock<IUnitOfWork>();

            _applications.Setup(r => r.Remove(It.IsAny<LoanApplication>()));
            _entity = LoanApplication.Create(1, 1, 1, 1);
            _applications
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_entity);
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

        /// <summary>
        /// The event now comes from <c>LoanApplication.Delete()</c> rather than being constructed by
        /// the handler, so this asserts the handler still causes it to be raised.
        /// </summary>
        [Test]
        public async Task DeleteApplicationCommand_WhenCalled_RaisesDeletedEventFromTheAggregate()
        {
            var handler = new DeleteApplicationCommandHandler(_applications.Object, _unitOfWork.Object);

            await handler.Handle(new DeleteApplicationCommand { Id = 1 }, default);

            Assert.That(_entity.DomainEvents.Any(e => e is ApplicationDeletedEvent), Is.True);
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
        public void DeleteApplicationCommand_WhenApplicationDisappearsAfterValidation_ThrowsAndPersistsNothing()
        {
            _applications
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LoanApplication?)null);

            var handler = new DeleteApplicationCommandHandler(_applications.Object, _unitOfWork.Object);

            Assert.That(
                async () => await handler.Handle(new DeleteApplicationCommand { Id = 1 }, default),
                Throws.InstanceOf<DomainValidationException>()
                      .With.Message.EqualTo("InvalidApplication"));

            _applications.Verify(r => r.Remove(It.IsAny<LoanApplication>()), Times.Never());
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }
    }
}
