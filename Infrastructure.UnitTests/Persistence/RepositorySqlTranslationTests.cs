using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// <see cref="Repositories.RepositoryTests"/> covers the generic repository against the in-memory
    /// provider, which evaluates almost anything. This proves the same members become valid T-SQL —
    /// the include shaper, a filtered+ordered list, and above all <c>PageAsync</c>, whose
    /// <c>CountAsync</c> runs over an already-ordered query (SQL Server rejects <c>ORDER BY</c> in a
    /// subquery without <c>TOP</c>, so EF has to strip it). Requires localhost\SQLEXPRESS with
    /// migrations applied; excluded from the normal run.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a real SQL Server instance; proves SQL translation, not just evaluation.")]
    public class RepositorySqlTranslationTests
    {
        // Currencies.Name is nvarchar(10).
        private const string CurrencyName = "REPOTEST";
        private const string LoanTypeName = "RepositorySqlTranslationLoanType";
        private const string CreatedBy = "repository-sql-translation";

        private static ApplicationDbContext CreateContext() => TestDb.SqlServer();

        [Test]
        public async Task EveryGenericMemberTranslatesToSql()
        {
            await using var context = CreateContext();
            var repository = new Repository<LoanApplication>(context);

            var currency = Currency.Create(CurrencyName);
            var loanType = LoanType.Create(LoanTypeName);
            context.Currencies.Add(currency);
            context.LoanTypes.Add(loanType);
            await context.SaveChangesAsync(default);

            var seeded = new List<LoanApplication>();
            for (var i = 0; i < 5; i++)
                seeded.Add(LoanApplication.Create(loanType.Id, 900 + i, currency.Id, 12, CreatedBy, DateTime.UtcNow.AddSeconds(i)));

            await repository.AddRangeAsync(seeded);
            await context.SaveChangesAsync(default);

            try
            {
                // Filter + ordering.
                var listed = await repository.ListAsync(
                    filter: a => a.CreatedBy == CreatedBy,
                    orderBy: q => q.OrderByDescending(a => a.Amount),
                    asNoTracking: true);
                Assert.That(listed.Select(a => a.Amount), Is.EqualTo(new[] { 904m, 903m, 902m, 901m, 900m }));

                // The include shaper, with a ThenInclude-capable chain.
                var withNavigations = await repository.FirstOrDefaultAsync(
                    a => a.CreatedBy == CreatedBy,
                    include: q => q.Include(a => a.Currency).Include(a => a.LoanType),
                    asNoTracking: true);
                Assert.That(withNavigations!.Currency!.Name, Is.EqualTo(CurrencyName));
                Assert.That(withNavigations.LoanType!.Name, Is.EqualTo(LoanTypeName));

                // PageAsync: COUNT over an ordered query, then the page.
                var (items, totalCount) = await repository.PageAsync(
                    pageIndex: 2,
                    pageSize: 2,
                    filter: a => a.CreatedBy == CreatedBy,
                    orderBy: q => q.OrderByDescending(a => a.Amount),
                    include: q => q.Include(a => a.Currency),
                    asNoTracking: true);
                Assert.That(totalCount, Is.EqualTo(5));
                Assert.That(items.Select(a => a.Amount), Is.EqualTo(new[] { 902m, 901m }));

                Assert.That(await repository.CountAsync(a => a.CreatedBy == CreatedBy), Is.EqualTo(5));
                Assert.That(await repository.AnyAsync(a => a.CreatedBy == CreatedBy), Is.True);
                Assert.That(await repository.AnyAsync(a => a.CreatedBy == "no-such-user"), Is.False);

                Assert.That(
                    await repository.SingleOrDefaultAsync(a => a.CreatedBy == CreatedBy && a.Amount == 903m, asNoTracking: true),
                    Is.Not.Null);

                // Composable escape hatch, projected — the shape a query handler would use.
                var amounts = await repository.QueryAsNoTracking()
                    .Where(a => a.CreatedBy == CreatedBy)
                    .OrderBy(a => a.Amount)
                    .Select(a => a.Amount)
                    .ToListAsync();
                Assert.That(amounts, Is.EqualTo(new[] { 900m, 901m, 902m, 903m, 904m }));

                // Detached update against a real row, including the RowVersion round trip.
                var detachedId = seeded[0].Id;
                await using (var updateContext = CreateContext())
                {
                    var updateRepository = new Repository<LoanApplication>(updateContext);
                    var detached = await updateRepository.QueryAsNoTracking().FirstAsync(a => a.Id == detachedId);
                    detached.Update(loanType.Id, 1234m, currency.Id, 24, CreatedBy, DateTime.UtcNow);
                    updateRepository.Update(detached);
                    await updateContext.SaveChangesAsync(default);
                }

                await using (var verifyContext = CreateContext())
                {
                    var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(detachedId);
                    Assert.That(reloaded!.Amount, Is.EqualTo(1234m));
                }
            }
            finally
            {
                // Reload in this context: the detached update bumped RowVersion, so the stale
                // instances would otherwise fail the concurrency check on delete.
                var toDelete = await context.LoanApplications
                    .Where(a => a.CreatedBy == CreatedBy)
                    .ToListAsync();
                foreach (var entity in toDelete)
                    context.Entry(entity).Reload();

                context.LoanApplications.RemoveRange(toDelete);
                await context.SaveChangesAsync(default);

                context.Currencies.Remove(currency);
                context.LoanTypes.Remove(loanType);
                await context.SaveChangesAsync(default);
            }
        }
    }
}
