using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence.Repositories;
using Moq;
using NUnit.Framework;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Application.UnitTests.LoanApplications.Commands
{
    [TestFixture]
    public class CreateLoanApplicationTests
    {
        private Mock<ICurrentUserService> _currentUserService;
        private Mock<IDateTime> _dateTime;
        private Mock<UnitOfWork> _unitOfWork;
        private Mock<ILoanApplicationRepository> _loanApplicationRepository;
        private Mock<IApplicationDbContext> _context;

        [SetUp]
        public void SetUp()
        {
            _currentUserService = new Mock<ICurrentUserService>();
            _dateTime = new Mock<IDateTime>();
            _loanApplicationRepository = new Mock<ILoanApplicationRepository>();
            _context = new Mock<IApplicationDbContext>();
            _unitOfWork= new Mock<UnitOfWork>(
                _context.Object, 
                It.IsAny<ICurrencyRepository>(), 
                It.IsAny<ILoanTypeRepository>(), 
                _loanApplicationRepository.Object);

            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _loanApplicationRepository.Setup(r => r.AddAsync(It.IsAny<LoanApplication>(), It.IsAny<CancellationToken>()));
            _unitOfWork.Setup(uow => uow.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(1));
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

            var handler = new CreateApplicationCommandhandler(_currentUserService.Object, _dateTime.Object, _unitOfWork.Object);

            // Act
            var result = await handler.Handle(command, default);

            // Assert

            // Ensure that result is int type
            Assert.That(result, Is.TypeOf<int>());
            // Ensure that repository AddAsync Method is called
            _loanApplicationRepository.Verify(r => r.AddAsync(It.IsAny<LoanApplication>(), It.IsAny<CancellationToken>()));
            // Ensure that unit of work SaveAsync Method is called
            _unitOfWork.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }
}
