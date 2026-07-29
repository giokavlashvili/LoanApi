using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Repositories
{
    [TestFixture]
    public class RepositoryTests
    {
        private static ApplicationDbContext CreateContext(string databaseName) =>
            new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(databaseName)
                    .Options,
                new Mock<IMediator>().Object);

        [Test]
        public async Task GetByIdAsync_WithExistingId_ReturnsTheEntity()
        {
            // Arrange — FindAsync(id, cancellationToken) binds the params overload and passes
            // the CancellationToken as a second key value; this only surfaces against a real
            // DbSet, never against a mocked repository.
            await using var context = CreateContext(Guid.NewGuid().ToString());
            var entity = LoanApplication.Create(1, 100, 1, 12, "userId", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            context.LoanApplications.Add(entity);
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
            // mutating it and saving is enough; calling repository.Update(entity) on top would
            // mark every column modified and rewrite the whole row for no reason.
            var databaseName = Guid.NewGuid().ToString();
            int entityId;

            await using (var context = CreateContext(databaseName))
            {
                var entity = LoanApplication.Create(1, 100, 1, 12, "userId", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                context.LoanApplications.Add(entity);
                await context.SaveChangesAsync(default);
                entityId = entity.Id;

                var repository = new Repository<LoanApplication>(context);
                var tracked = await repository.GetByIdAsync(entityId, CancellationToken.None);
                tracked!.Update(2, 250, 3, 24, "userId", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

                // Act — no repository.Update(tracked) call here.
                await context.SaveChangesAsync(default);
            }

            // Assert — a brand new context/change tracker over the same in-memory store, so this
            // proves the mutation actually reached the "database" rather than just the in-memory
            // object graph the first context already held a reference to.
            await using var verifyContext = CreateContext(databaseName);
            var reloaded = await new Repository<LoanApplication>(verifyContext).GetByIdAsync(entityId, CancellationToken.None);
            Assert.That(reloaded!.Amount, Is.EqualTo(250));
            Assert.That(reloaded.LoanTypeId, Is.EqualTo(2));
        }
    }
}
