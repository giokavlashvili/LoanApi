﻿using Application.Common.Interfaces;
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
    public class DeleteLoanApplicationTests
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
            _unitOfWork = new Mock<UnitOfWork>(
                _context.Object,
                It.IsAny<ICurrencyRepository>(),
                It.IsAny<ILoanTypeRepository>(),
                _loanApplicationRepository.Object);

            _currentUserService.Setup(u => u.UserId).Returns("userId");
            _loanApplicationRepository.Setup(r => r.Remove(It.IsAny<LoanApplication>()));
            _loanApplicationRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(LoanApplication.Create(1,1,1,1,"UserId", DateTime.Now)));
            _unitOfWork.Setup(uow => uow.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(1));
        }

        [Test]
        public async Task DeleteApplicationCommand_WhenCalled_DeleteApplication()
        {
            // Arrange
            var command = new DeleteApplicationCommand()
            {
                Id = 1
            };

            var handler = new DeleteApplicationCommandHandler(_unitOfWork.Object);

            // Act
            await handler.Handle(command, default);

            // Assert
            // Ensure that repository Remove Method is called
            _loanApplicationRepository.Verify(r => r.Remove(It.IsAny<LoanApplication>()));
            // Ensure that unit of work SaveAsync Method is called
            _unitOfWork.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }
}
