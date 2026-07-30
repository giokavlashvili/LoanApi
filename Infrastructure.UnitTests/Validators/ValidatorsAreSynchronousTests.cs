using Application.LoanApplications.Commands;
using Application.LoanApplications.Validators;
using FluentValidation;
using Infrastructure.Persistence;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Validators
{
    /// <summary>
    /// <c>WebApi</c> calls <c>AddFluentValidationAutoValidation()</c>, so MVC's model-validation
    /// pipeline runs every validator in addition to <c>ValidationBehavior</c> — and that pipeline is
    /// synchronous. A single <c>MustAsync</c>/<c>CustomAsync</c> rule therefore makes MVC throw
    /// <c>AsyncValidatorInvokedSynchronouslyException</c> and the endpoint returns 500 before the
    /// handler is reached. Nothing else in the suite can see this: the handler tests never construct
    /// a validator, and calling <c>ValidateAsync</c> directly succeeds either way.
    ///
    /// Calling the **synchronous** <c>Validate</c> is precisely what MVC does, so that is the check.
    /// These run against a real context because the rules query.
    /// </summary>
    [TestFixture]
    public class ValidatorsAreSynchronousTests
    {
        // Assigned in SetUp before every test; null! rather than a file-wide CS8618 pragma, which
        // would also hide a genuinely uninitialised field.
        private ApplicationDbContext _context = null!;

        [SetUp]
        public void SetUp() =>
            _context = TestDb.InMemory();

        [TearDown]
        public void TearDown() => _context.Dispose();

        private void AssertRunsSynchronously<T>(IValidator<T> validator, T instance) =>
            Assert.DoesNotThrow(
                () => validator.Validate(instance),
                $"{validator.GetType().Name} contains an asynchronous rule. MVC's automatic " +
                "validation cannot invoke one, so the endpoint would 500. Use Must/Custom, not " +
                "MustAsync/CustomAsync.");

        [Test]
        public void CreateApplicationCommandValidator_RunsUnderSynchronousValidation() =>
            AssertRunsSynchronously(
                new CreateApplicationCommandValidator(_context, TestDb.Localizer()),
                new CreateApplicationCommand { CurrencyId = 1, LoanTypeId = 1, Amount = 1, PeriodPerMonth = 1 });

        [Test]
        public void UpdateApplicationCommandValidator_RunsUnderSynchronousValidation() =>
            AssertRunsSynchronously(
                new UpdateApplicationCommandValidator(_context, TestDb.Localizer()),
                new UpdateApplicationCommand { Id = 1, CurrencyId = 1, LoanTypeId = 1, Amount = 1, PeriodPerMonth = 1 });

        [Test]
        public void UpdateApplicationStatusCommandValidator_RunsUnderSynchronousValidation() =>
            AssertRunsSynchronously(
                new UpdateApplicationStatusCommandValidator(_context, TestDb.Localizer()),
                new UpdateApplicationStatusCommand { Id = 1 });

        [Test]
        public void DeleteApplicationCommandValidator_RunsUnderSynchronousValidation() =>
            AssertRunsSynchronously(
                new DeleteApplicationCommandValidator(_context, TestDb.Localizer()),
                new DeleteApplicationCommand { Id = 1 });
    }
}
