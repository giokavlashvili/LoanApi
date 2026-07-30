using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Repositories
{
    [TestFixture]
    public class RepositoryTests
    {
        private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ApplicationDbContext CreateContext(string databaseName) => TestDb.InMemory(databaseName);

        private static LoanApplication AddApplication(
            ApplicationDbContext context,
            int loanTypeId,
            decimal amount,
            int currencyId,
            int period,
            DateTime created) =>
            TestDb.AddApplication(context, loanTypeId, amount, currencyId, period, "userId", created);

        /// <summary>Seeds through its own context so the querying context starts with a clean tracker.</summary>
        private static async Task<string> SeedAsync(int applicationCount, bool withReferenceData = false)
        {
            var databaseName = Guid.NewGuid().ToString();
            await using var context = CreateContext(databaseName);

            var currencyId = 1;
            var loanTypeId = 1;

            if (withReferenceData)
            {
                var currency = Currency.Create("GEL");
                var loanType = LoanType.Create("Mortgage");
                context.Currencies.Add(currency);
                context.LoanTypes.Add(loanType);
                await context.SaveChangesAsync(default);
                currencyId = currency.Id;
                loanTypeId = loanType.Id;
            }

            for (var i = 0; i < applicationCount; i++)
                AddApplication(context, loanTypeId, 100 + i, currencyId, 12, Created.AddMinutes(i));

            await context.SaveChangesAsync(default);
            return databaseName;
        }

        [Test]
        public async Task GetByIdAsync_WithExistingId_ReturnsTheEntity()
        {
            // Arrange — FindAsync(id, cancellationToken) binds the params overload and passes
            // the CancellationToken as a second key value; this only surfaces against a real
            // DbSet, never against a mocked repository.
            await using var context = CreateContext(Guid.NewGuid().ToString());
            var entity = AddApplication(context, 1, 100, 1, 12, Created);
            await context.SaveChangesAsync(default);

            var repository = new Repository<LoanApplication>(context);

            // Act
            var result = await repository.GetByIdAsync(entity.Id, CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Id, Is.EqualTo(entity.Id));
        }

        [Test]
        public async Task Update_WithoutCallingRepositoryUpdate_MutationIsPersisted()
        {
            // Arrange — the entity is already tracked once it's loaded through this context, so
            // mutating it and saving is enough.
            var databaseName = Guid.NewGuid().ToString();
            int entityId;

            await using (var context = CreateContext(databaseName))
            {
                var entity = AddApplication(context, 1, 100, 1, 12, Created);
                await context.SaveChangesAsync(default);
                entityId = entity.Id;

                var repository = new Repository<LoanApplication>(context);
                var tracked = await repository.GetByIdAsync(entityId, CancellationToken.None);
                tracked!.Update(2, 250, 3, 24);

                // Act — no repository.Update(tracked) call here.
                await context.SaveChangesAsync(default);
            }

            // Assert — a brand new context/change tracker over the same in-memory store.
            await using var verifyContext = CreateContext(databaseName);
            var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(entityId, CancellationToken.None);
            Assert.That(reloaded!.Amount, Is.EqualTo(250));
            Assert.That(reloaded.LoanTypeId, Is.EqualTo(2));
        }

        [Test]
        public async Task Update_OnATrackedAggregate_LeavesUntouchedColumnsUnmodified()
        {
            // Update() deliberately does nothing for a tracked entity, and this asserts the no-op
            // rather than just "it didn't throw": if it forwarded to DbSet.Update, *every* property
            // would be flagged modified and the whole row rewritten — including Created/CreatedBy,
            // which this mutation never touches, and the RowVersion token.
            var databaseName = Guid.NewGuid().ToString();
            int entityId;

            await using (var context = CreateContext(databaseName))
            {
                var entity = AddApplication(context, 1, 100, 1, 12, Created);
                await context.SaveChangesAsync(default);
                entityId = entity.Id;

                var repository = new Repository<LoanApplication>(context);
                var tracked = await repository.GetByIdAsync(entityId, CancellationToken.None);
                tracked!.Update(2, 250, 3, 24);

                repository.Update(tracked);

                var entry = context.Entry(tracked);
                Assert.That(entry.Property(nameof(LoanApplication.Amount)).IsModified, Is.True);
                Assert.That(entry.Property(nameof(LoanApplication.Created)).IsModified, Is.False,
                    "Created was not mutated; a full-row Update would have marked it modified.");
                Assert.That(entry.Property(nameof(LoanApplication.CreatedBy)).IsModified, Is.False);
                Assert.That(entry.Properties.Count(p => p.IsModified), Is.LessThan(entry.Properties.Count()));

                await context.SaveChangesAsync(default);
            }

            await using var verifyContext = CreateContext(databaseName);
            var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(entityId, CancellationToken.None);
            Assert.That(reloaded!.Amount, Is.EqualTo(250));
        }

        [Test]
        public async Task PageAsync_WithNoOrderBy_StillPagesDeterministically()
        {
            // Skip/Take with no ordering makes EF emit ORDER BY (SELECT 1), which lets the server
            // return rows in any order — the same row can land on two pages while another is never
            // returned. PageAsync falls back to the key, so the pages must partition the table.
            var databaseName = await SeedAsync(applicationCount: 5);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            var (firstPage, total) = await repository.PageAsync(1, 2, asNoTracking: true);
            var (secondPage, _) = await repository.PageAsync(2, 2, asNoTracking: true);
            var (thirdPage, _) = await repository.PageAsync(3, 2, asNoTracking: true);

            var ids = firstPage.Concat(secondPage).Concat(thirdPage).Select(a => a.Id).ToList();

            Assert.That(total, Is.EqualTo(5));
            Assert.That(ids, Is.Unique, "A page boundary repeated a row — the paging is not stable.");
            Assert.That(ids, Has.Count.EqualTo(5), "Every row must appear on exactly one page.");
            Assert.That(ids, Is.Ordered, "The key fallback should yield ascending ids.");
        }

        [Test]
        public async Task Repository_WorksForAnEntityThatIsNotAnAggregateRoot()
        {
            // The constraint is BaseEntity, not IAggregateRoot, precisely so this compiles: Currency
            // is reference data and deliberately not a root, but still gets the generic repository.
            var databaseName = Guid.NewGuid().ToString();

            await using (var context = CreateContext(databaseName))
            {
                var repository = new Repository<Currency>(context);
                await repository.AddRangeAsync(new[] { Currency.Create("GEL"), Currency.Create("USD") });
                await context.SaveChangesAsync(default);
            }

            await using var verifyContext = CreateContext(databaseName);
            var currencies = new Repository<Currency>(verifyContext);

            Assert.That(await currencies.CountAsync(), Is.EqualTo(2));
            Assert.That(await currencies.AnyAsync(c => c.Name == "USD"), Is.True);

            var listed = await currencies.ListAsync(orderBy: q => q.OrderBy(c => c.Name), asNoTracking: true);
            Assert.That(listed.Select(c => c.Name), Is.EqualTo(new[] { "GEL", "USD" }));
        }

        [Test]
        public async Task Update_WithADetachedAggregate_PersistsTheMutation()
        {
            // The case Update() exists for: an aggregate read without tracking (or arriving from
            // another context) has no tracked original, so mutating it alone would be lost.
            var databaseName = await SeedAsync(applicationCount: 1);
            int entityId;

            await using (var context = CreateContext(databaseName))
            {
                var repository = new Repository<LoanApplication>(context);
                var detached = await repository.QueryAsNoTracking().FirstAsync();
                entityId = detached.Id;

                detached.Update(2, 250, 3, 24);
                repository.Update(detached);

                await context.SaveChangesAsync(default);
            }

            await using var verifyContext = CreateContext(databaseName);
            var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(entityId, CancellationToken.None);
            Assert.That(reloaded!.Amount, Is.EqualTo(250), "A detached aggregate needs Update() for its mutation to reach the database.");
            Assert.That(reloaded.LoanTypeId, Is.EqualTo(2));
        }

        [Test]
        public async Task ListAsync_WithTheIncludeShaper_LoadsTheNavigation()
        {
            // The typed replacement for includeProperties: "Currency". Queried from a fresh
            // context so navigation fixup from tracked reference data cannot fake the result.
            var databaseName = await SeedAsync(applicationCount: 1, withReferenceData: true);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            var withoutInclude = await repository.ListAsync(asNoTracking: true);
            var withInclude = await repository.ListAsync(
                include: q => q.Include(a => a.Currency).Include(a => a.LoanType),
                asNoTracking: true);

            Assert.That(withoutInclude[0].Currency, Is.Null);
            Assert.That(withInclude[0].Currency!.Name, Is.EqualTo("GEL"));
            Assert.That(withInclude[0].LoanType!.Name, Is.EqualTo("Mortgage"));
        }

        [Test]
        public async Task ListAsync_AppliesFilterAndOrdering()
        {
            var databaseName = await SeedAsync(applicationCount: 5);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            var result = await repository.ListAsync(
                filter: a => a.Amount >= 102,
                orderBy: q => q.OrderByDescending(a => a.Amount),
                asNoTracking: true);

            Assert.That(result.Select(a => a.Amount), Is.EqualTo(new[] { 104m, 103m, 102m }));
        }

        [Test]
        public async Task PageAsync_ReturnsThatSliceAndTheUnpagedTotal()
        {
            var databaseName = await SeedAsync(applicationCount: 5);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            var (items, totalCount) = await repository.PageAsync(
                pageIndex: 2,
                pageSize: 2,
                orderBy: q => q.OrderByDescending(a => a.Created).ThenByDescending(a => a.Id),
                asNoTracking: true);

            // Amounts are 100 + i and Created ascends with i, so newest-first puts 104/103 on
            // page one and 102/101 on page two. TotalCount ignores the paging.
            Assert.That(totalCount, Is.EqualTo(5));
            Assert.That(items.Select(a => a.Amount), Is.EqualTo(new[] { 102m, 101m }));
        }

        [Test]
        public async Task PageAsync_WithAFilter_CountsOnlyTheFilteredRows()
        {
            var databaseName = await SeedAsync(applicationCount: 5);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            var (items, totalCount) = await repository.PageAsync(
                pageIndex: 1,
                pageSize: 10,
                filter: a => a.Amount >= 103,
                orderBy: q => q.OrderBy(a => a.Amount),
                asNoTracking: true);

            Assert.That(totalCount, Is.EqualTo(2));
            Assert.That(items, Has.Count.EqualTo(2));
        }

        [TestCase(0, 5, "InvalidPageNumber")]
        [TestCase(1, 0, "InvalidPageSize")]
        public async Task PageAsync_WithANonPositiveArgument_ThrowsWithALocalizationKey(
            int pageIndex, int pageSize, string expectedKey)
        {
            await using var context = CreateContext(Guid.NewGuid().ToString());
            var repository = new Repository<LoanApplication>(context);

            // The message is a key resolved against localization.json at the WebApi boundary, not
            // user-facing English embedded in the persistence layer.
            Assert.That(
                async () => await repository.PageAsync(pageIndex, pageSize),
                Throws.InstanceOf<DomainValidationException>().With.Message.EqualTo(expectedKey));
        }

        [Test]
        public async Task AnyAsyncAndCountAsync_WithAndWithoutAFilter()
        {
            var databaseName = await SeedAsync(applicationCount: 3);

            await using var context = CreateContext(databaseName);
            var repository = new Repository<LoanApplication>(context);

            Assert.That(await repository.AnyAsync(), Is.True);
            Assert.That(await repository.AnyAsync(a => a.Amount == 101), Is.True);
            Assert.That(await repository.AnyAsync(a => a.Amount == 9999), Is.False);
            Assert.That(await repository.CountAsync(), Is.EqualTo(3));
            Assert.That(await repository.CountAsync(a => a.Amount >= 101), Is.EqualTo(2));
        }

        [Test]
        public async Task AddRangeAsync_ThenRemoveRange_RoundTrips()
        {
            var databaseName = Guid.NewGuid().ToString();

            await using (var context = CreateContext(databaseName))
            {
                var repository = new Repository<LoanApplication>(context);

                // Through AddRangeAsync rather than the seeding helper, because exercising that
                // repository method is the point of this case. CreatedBy is IsRequired() in
                // LoanApplicationConfiguration, so it still has to be stamped before the save.
                var batch = new[]
                {
                    LoanApplication.Create(1, 10, 1, 12),
                    LoanApplication.Create(1, 20, 1, 12)
                };
                await repository.AddRangeAsync(batch);

                foreach (var entity in batch)
                {
                    var entry = context.Entry(entity);
                    entry.Property(e => e.Created).CurrentValue = Created;
                    entry.Property(e => e.CreatedBy).CurrentValue = "userId";
                }

                await context.SaveChangesAsync(default);
            }

            await using (var context = CreateContext(databaseName))
            {
                var repository = new Repository<LoanApplication>(context);
                Assert.That(await repository.CountAsync(), Is.EqualTo(2));

                repository.RemoveRange(await repository.ListAsync());
                await context.SaveChangesAsync(default);
            }

            await using var verifyContext = CreateContext(databaseName);
            Assert.That(await new Repository<LoanApplication>(verifyContext).CountAsync(), Is.EqualTo(0));
        }

        [Test]
        public async Task QueryAsNoTracking_DoesNotPersistMutations()
        {
            // Documents the trade-off of the untracked read, so nobody has to discover it in
            // production: without Update(), a mutation on an untracked aggregate is silently lost.
            var databaseName = await SeedAsync(applicationCount: 1);
            int entityId;

            await using (var context = CreateContext(databaseName))
            {
                var untracked = await new Repository<LoanApplication>(context).QueryAsNoTracking().FirstAsync();
                entityId = untracked.Id;
                untracked.Update(9, 999, 9, 99);
                await context.SaveChangesAsync(default);
            }

            await using var verifyContext = CreateContext(databaseName);
            var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(entityId, CancellationToken.None);
            Assert.That(reloaded!.Amount, Is.EqualTo(100));
        }
    }
}
