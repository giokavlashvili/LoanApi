using Application.Currencies.Queries;
using Application.LoanApplications.Queries;
using Application.LoanTypes.Queries;
using Domain.Entities;
using Infrastructure.Persistence;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Queries
{
    /// <summary>
    /// The query handlers project with Select, which builds an expression tree the provider
    /// translates. A Mock&lt;IApplicationDbContext&gt; cannot exercise that at all — it would return a
    /// list the projection never touches — so these run against a real context on the in-memory
    /// provider. The failure they exist to catch is a silently empty LoanType/Currency in the list
    /// response, which is what a projection that cannot reach a navigation produces.
    /// </summary>
    [TestFixture]
    public class ProjectionQueryTests
    {
        private static readonly DateTime BaseTime = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);


        private static ApplicationDbContext CreateContext(string databaseName) => TestDb.InMemory(databaseName);

        /// <summary>
        /// Seeds through its own context and disposes it, so the querying context starts with an
        /// empty change tracker. Otherwise a tracked object graph could satisfy the navigations
        /// even if the projection never joined.
        /// </summary>
        private static async Task<string> SeedAsync(int applicationCount)
        {
            var databaseName = Guid.NewGuid().ToString();

            await using var context = CreateContext(databaseName);

            var currency = Currency.Create("GEL");
            var loanType = LoanType.Create("Mortgage");
            context.Currencies.Add(currency);
            context.LoanTypes.Add(loanType);
            await context.SaveChangesAsync(default);

            for (var i = 0; i < applicationCount; i++)
            {
                TestDb.AddApplication(
                    context, loanType.Id, 100 + i, currency.Id, 12, "userId", BaseTime.AddMinutes(i));
            }

            await context.SaveChangesAsync(default);

            return databaseName;
        }

        [Test]
        public async Task GetCurrencies_ProjectsEveryRowToItsDto()
        {
            // Arrange
            var databaseName = await SeedAsync(applicationCount: 0);
            await using var context = CreateContext(databaseName);

            // Act
            var result = await new GetCurrenciesQueryHandler(context)
                .Handle(new GetCurrenciesQuery(), CancellationToken.None);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("GEL"));
            Assert.That(result[0].Id, Is.GreaterThan(0));
        }

        [Test]
        public async Task GetLoanTypes_ProjectsEveryRowToItsDto()
        {
            // Arrange
            var databaseName = await SeedAsync(applicationCount: 0);
            await using var context = CreateContext(databaseName);

            // Act
            var result = await new GetLoanTypesQueryHandler(context)
                .Handle(new GetLoanTypesQuery(), CancellationToken.None);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("Mortgage"));
            Assert.That(result[0].Id, Is.GreaterThan(0));
        }

        [Test]
        public async Task GetApplications_ResolvesTheNavigationNamesThroughTheProjection()
        {
            // Arrange
            var databaseName = await SeedAsync(applicationCount: 1);
            await using var context = CreateContext(databaseName);

            // Act
            var result = await new GetApplicationsQueryHandler(context)
                .Handle(new GetApplicationsQuery(), CancellationToken.None);

            // Assert — the Include pair is gone; these two names now come from the projection's
            // own joins, and an empty string here is the regression this test guards.
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].LoanType, Is.EqualTo("Mortgage"));
            Assert.That(result.Items[0].Currency, Is.EqualTo("GEL"));
            Assert.That(result.Items[0].Amount, Is.EqualTo(100));
            Assert.That(result.Items[0].PeriodPerMonth, Is.EqualTo(12));
        }

        [Test]
        public async Task GetApplications_SecondPage_ReturnsThatSliceAndTheFullCount()
        {
            // Arrange — five applications one minute apart, ordered newest first.
            var databaseName = await SeedAsync(applicationCount: 5);
            await using var context = CreateContext(databaseName);

            // Act
            var result = await new GetApplicationsQueryHandler(context)
                .Handle(new GetApplicationsQuery { PageNumber = 2, PageSize = 2 }, CancellationToken.None);

            // Assert — the count covers the whole table, the items only the requested page, and
            // both derive from one query definition rather than two repository methods.
            Assert.That(result.TotalCount, Is.EqualTo(5));
            Assert.That(result.TotalPages, Is.EqualTo(3));
            Assert.That(result.PageNumber, Is.EqualTo(2));
            Assert.That(result.HasPreviousPage, Is.True);
            Assert.That(result.HasNextPage, Is.True);

            // Amount is 100 + i and Created is BaseTime + i minutes, so descending by Created puts
            // 104 and 103 on page one and 102, 101 on page two.
            Assert.That(result.Items.Select(i => i.Amount), Is.EqualTo(new[] { 102m, 101m }));
        }
    }
}
