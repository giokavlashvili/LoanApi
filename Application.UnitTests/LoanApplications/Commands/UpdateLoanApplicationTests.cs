using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Repositories;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.

namespace Application.UnitTests.LoanApplications.Commands
{
    [TestFixture]
    public class UpdateLoanApplicationTests
    {
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IDateTime> _dateTime;
        private Mock<UnitOfWork> _unitOfWork;
        private Mock<ILoanApplicationRepository> _loanApplicationRepository;
        private Mock<IApplicationDbContext> _context;
        private LoanApplication _entity;

        [SetUp]
        public void SetUp()
        {
            _currentUserService = new Mock<ICurrentUserService>();
            _dateTime = new Mock<IDateTime>();
            _loanApplicationRepository = new Mock<ILoanApplicationRepository>();
            _context = new Mock<IApplicationDbContext>();
            _unitOfWork = new Mock<UnitOfWork>(
                _context.Object,
                It.IsAny<ICurrencyRepository>(),
                It.IsAny<ILoanTypeRepository>(),
                _loanApplicationRepository.Object,
                It.IsAny<IOtpVerificationRepository>());

            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _entity = LoanApplication.Create(1, 100, 5, 6, "UserId", new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc));
            _loanApplicationRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(_entity);
            _unitOfWork.Setup(uow => uow.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(1));
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

            var handler = new UpdateApplicationCommandHandler(_currentUserService.Object, _dateTime.Object, _unitOfWork.Object);

            // Act
            await handler.Handle(command, default);

            // Assert — no repository.Update call: the aggregate loaded by GetByIdAsync is
            // already tracked, so mutating it in place is what actually persists the change.
            Assert.That(_entity.Amount, Is.EqualTo(command.Amount));
            Assert.That(_entity.CurrencyId, Is.EqualTo(command.CurrencyId));
            Assert.That(_entity.LoanTypeId, Is.EqualTo(command.LoanTypeId));
            Assert.That(_entity.PeriodPerMonth, Is.EqualTo(command.PeriodPerMonth));
            _unitOfWork.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }
}
