using Application.Currencies.Queries;
using Application.LoanApplications.Commands;
using Application.LoanApplications.Queries;
using Application.LoanApplications.Validators;
using Application.LoanTypes.Queries;
using Domain.Entities;
using Infrastructure.Persistence;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The in-memory provider evaluates almost anything, so ProjectionQueryTests passing there
    /// says nothing about whether these expressions become valid T-SQL. Three specific risks the
    /// in-memory provider cannot see: CountAsync over an already-ordered query (SQL Server rejects
    /// ORDER BY in a subquery without TOP, so EF has to strip it), the conditional MapFrom
    /// expressions in LoanApplicationDto, and the navigation null checks the update validator now
    /// projects instead of Include-ing. Requires localhost\SQLEXPRESS with migrations applied;
    /// excluded from the normal run.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a real SQL Server instance; proves SQL translation, not just evaluation.")]
    public class ProjectionSqlTranslationTests
    {
        // Currencies.Name is nvarchar(10); LoanTypes.Name allows 200.
        private const string CurrencyName = "PROJTEST";
        private const string LoanTypeName = "ProjectionTestLoanType";

        private static ApplicationDbContext CreateContext() => TestDb.SqlServer();


        [Test]
        public async Task EveryProjectionTranslatesToSqlAndResolvesItsNavigations()
        {
            await using var context = CreateContext();

            var currency = Currency.Create(CurrencyName);
            var loanType = LoanType.Create(LoanTypeName);
            context.Currencies.Add(currency);
            context.LoanTypes.Add(loanType);
            await context.SaveChangesAsync(default);

            var application = LoanApplication.Create(
                loanType.Id, 4321m, currency.Id, 18, "projection-test-user", DateTime.UtcNow);
            context.LoanApplications.Add(application);
            await context.SaveChangesAsync(default);

            try
            {
                // The two reference-data projections.
                var currencies = await new GetCurrenciesQueryHandler(TestDb.Mapper(), context)
                    .Handle(new GetCurrenciesQuery(), CancellationToken.None);
                Assert.That(currencies.Select(c => c.Name), Does.Contain(CurrencyName));

                var loanTypes = await new GetLoanTypesQueryHandler(TestDb.Mapper(), context)
                    .Handle(new GetLoanTypesQuery(), CancellationToken.None);
                Assert.That(loanTypes.Select(t => t.Name), Does.Contain(LoanTypeName));

                // The paginated projection: CountAsync over the ordered query, then the page. The
                // row just inserted has the newest Created, so it is first on page one.
                var page = await new GetApplicationsQueryHandler(TestDb.Mapper(), context)
                    .Handle(new GetApplicationsQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

                Assert.That(page.TotalCount, Is.GreaterThan(0));

                var projected = page.Items.Single(i => i.Id == application.Id);
                // An empty name here is the failure mode the phase warns about: a projection that
                // reached the row but not the joined reference data.
                Assert.That(projected.LoanType, Is.EqualTo(LoanTypeName));
                Assert.That(projected.Currency, Is.EqualTo(CurrencyName));
                Assert.That(projected.Amount, Is.EqualTo(4321m));

                // The update validator's navigation null checks, through the real validator rather
                // than a copy of its query.
                var validator = new UpdateApplicationCommandValidator(context, TestDb.Localizer());

                var valid = await validator.ValidateAsync(new UpdateApplicationCommand
                {
                    Id = application.Id,
                    LoanTypeId = loanType.Id,
                    Amount = 1,
                    CurrencyId = currency.Id,
                    PeriodPerMonth = 1
                });
                Assert.That(valid.IsValid, Is.True, "A seeded application with both navigations must validate.");

                var missing = await validator.ValidateAsync(new UpdateApplicationCommand { Id = -1 });
                Assert.That(missing.IsValid, Is.False);
                Assert.That(missing.Errors.Single().ErrorMessage, Is.EqualTo("InvalidApplication"));

                // The existence rules on the other three validators.
                var deleteValidator = new DeleteApplicationCommandValidator(context, TestDb.Localizer());
                Assert.That((await deleteValidator.ValidateAsync(new DeleteApplicationCommand { Id = application.Id })).IsValid, Is.True);
                Assert.That((await deleteValidator.ValidateAsync(new DeleteApplicationCommand { Id = -1 })).IsValid, Is.False);

                var createValidator = new CreateApplicationCommandValidator(context, TestDb.Localizer());
                Assert.That((await createValidator.ValidateAsync(new CreateApplicationCommand
                {
                    CurrencyId = currency.Id,
                    LoanTypeId = loanType.Id,
                    Amount = 1,
                    PeriodPerMonth = 1
                })).IsValid, Is.True);
                Assert.That((await createValidator.ValidateAsync(new CreateApplicationCommand
                {
                    CurrencyId = -1,
                    LoanTypeId = -1,
                    Amount = 1,
                    PeriodPerMonth = 1
                })).IsValid, Is.False);
            }
            finally
            {
                context.LoanApplications.Remove(application);
                await context.SaveChangesAsync(default);
                context.Currencies.Remove(currency);
                context.LoanTypes.Remove(loanType);
                await context.SaveChangesAsync(default);
            }
        }
    }
}
