using Application.Authenticate.Commands;
using Application.Authenticate.Validators;
using Application.Common.Interfaces;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Validators;
using FluentValidation;
using Infrastructure.Persistence;
using Moq;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Validators
{
    /// <summary>
    /// <c>ValidationBehavior</c> invokes validators via <c>ValidateAsync</c>. Lookup rules use
    /// <c>MustAsync</c>/<c>CustomAsync</c> so EF and Identity I/O can await without blocking.
    /// These smoke tests exercise that path against a real context (and a mocked identity service).
    /// </summary>
    [TestFixture]
    public class ValidatorLookupAsyncTests
    {
        // Assigned in SetUp before every test; null! rather than a file-wide CS8618 pragma, which
        // would also hide a genuinely uninitialised field.
        private ApplicationDbContext _context = null!;

        [SetUp]
        public void SetUp() =>
            _context = TestDb.InMemory();

        [TearDown]
        public void TearDown() => _context.Dispose();

        private static async Task AssertRunsAsync<T>(IValidator<T> validator, T instance)
        {
            // ValidationBehavior calls ValidateAsync; this smoke test proves I/O rules await cleanly.
            var result = await validator.ValidateAsync(instance);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public Task CreateApplicationCommandValidator_RunsUnderValidateAsync() =>
            AssertRunsAsync(
                new CreateApplicationCommandValidator(_context, TestDb.Localizer()),
                new CreateApplicationCommand { CurrencyId = 1, LoanTypeId = 1, Amount = 1, PeriodPerMonth = 1 });

        [Test]
        public Task UpdateApplicationCommandValidator_RunsUnderValidateAsync() =>
            AssertRunsAsync(
                new UpdateApplicationCommandValidator(_context, TestDb.Localizer()),
                new UpdateApplicationCommand { Id = 1, CurrencyId = 1, LoanTypeId = 1, Amount = 1, PeriodPerMonth = 1 });

        [Test]
        public Task UpdateApplicationStatusCommandValidator_RunsUnderValidateAsync() =>
            AssertRunsAsync(
                new UpdateApplicationStatusCommandValidator(_context, TestDb.Localizer()),
                new UpdateApplicationStatusCommand { Id = 1 });

        [Test]
        public Task DeleteApplicationCommandValidator_RunsUnderValidateAsync() =>
            AssertRunsAsync(
                new DeleteApplicationCommandValidator(_context, TestDb.Localizer()),
                new DeleteApplicationCommand { Id = 1 });

        [Test]
        public Task RegisterUserCommandValidator_RunsUnderValidateAsync()
        {
            var identity = new Mock<IIdentityService>();
            identity.Setup(s => s.IsUserNameAllowed(It.IsAny<string>())).Returns(true);
            identity.Setup(s => s.UserExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            return AssertRunsAsync(
                new RegisterUserCommandValidator(identity.Object),
                new RegisterUserCommand
                {
                    UserName = "newuser",
                    FirstName = "A",
                    LastName = "B",
                    Password = "Password1",
                    ConfirmPassword = "Password1",
                    PersonalNumber = "12345678901",
                    PhoneNumber = "+995555000000"
                });
        }
    }
}
