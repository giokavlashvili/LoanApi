using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The interceptor is the only thing populating audit columns now, so a silent failure here
    /// means every row is written with no audit trail and nothing else notices.
    /// </summary>
    [TestFixture]
    public class AuditableEntityInterceptorTests
    {
        private static readonly DateTime CreatedAt = new(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime ModifiedAt = new(2026, 7, 30, 15, 30, 0, DateTimeKind.Utc);

        /// <summary>
        /// Built the same way <c>AddInfrastructureServices</c> builds it — through
        /// <c>AddInterceptors</c> — so the test exercises the real attachment path and not a
        /// hand-rolled call to <c>Stamp</c>.
        /// </summary>
        private static ApplicationDbContext ContextWithInterceptor(
            string databaseName,
            string? userId,
            DateTime now)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(userId);

            var dateTime = new Mock<IDateTime>();
            dateTime.Setup(d => d.UtcNow).Returns(now);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditableEntityInterceptor(currentUser.Object, dateTime.Object))
                .Options;

            return new ApplicationDbContext(options, new Mock<IMediator>().Object);
        }

        [Test]
        public async Task Added_GetsCreatedAndCreatedBy_WithoutTheHandlerPassingThem()
        {
            var databaseName = TestDb.NewDatabaseName();

            await using (var context = ContextWithInterceptor(databaseName, "creator-id", CreatedAt))
            {
                // Note what is *not* passed: the factory takes no user id and no timestamp.
                context.LoanApplications.Add(LoanApplication.Create(1, 500, 1, 12));

                await context.SaveChangesAsync();
            }

            // A second context over the same store, so this reads what was persisted rather than
            // the first context's tracked instance.
            await using (var context = ContextWithInterceptor(databaseName, "creator-id", CreatedAt))
            {
                var persisted = await context.LoanApplications.SingleAsync();

                Assert.That(persisted.Created, Is.EqualTo(CreatedAt));
                Assert.That(persisted.CreatedBy, Is.EqualTo("creator-id"));
                Assert.That(persisted.LastModified, Is.Null);
                Assert.That(persisted.LastModifiedBy, Is.Null);
            }
        }

        [Test]
        public async Task Modified_GetsLastModified_AndDoesNotOverwriteCreated()
        {
            var databaseName = TestDb.NewDatabaseName();

            await using (var context = ContextWithInterceptor(databaseName, "creator-id", CreatedAt))
            {
                context.LoanApplications.Add(LoanApplication.Create(1, 500, 1, 12));
                await context.SaveChangesAsync();
            }

            // Different user, later clock — so an overwritten Created would be unmistakable.
            await using (var context = ContextWithInterceptor(databaseName, "modifier-id", ModifiedAt))
            {
                var entity = await context.LoanApplications.SingleAsync();

                entity.UpdateStatus(LoanStatus.InProcess);

                await context.SaveChangesAsync();
            }

            await using (var context = ContextWithInterceptor(databaseName, "modifier-id", ModifiedAt))
            {
                var persisted = await context.LoanApplications.SingleAsync();

                Assert.That(persisted.LastModified, Is.EqualTo(ModifiedAt));
                Assert.That(persisted.LastModifiedBy, Is.EqualTo("modifier-id"));

                // The whole point of the assertion: an update must not rewrite provenance.
                Assert.That(persisted.Created, Is.EqualTo(CreatedAt));
                Assert.That(persisted.CreatedBy, Is.EqualTo("creator-id"));
            }
        }

        /// <summary>
        /// <c>OtpVerification.Create</c> keeps its own <c>created</c> parameter and the resend
        /// cooldown reads that value back, so the interceptor must fill in only what the entity
        /// left unset rather than stamping over it.
        /// </summary>
        [Test]
        public async Task Added_DoesNotOverwriteAuditFieldsTheEntitySetItself()
        {
            var databaseName = TestDb.NewDatabaseName();
            var entityChoseThis = new DateTime(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc);

            await using (var context = ContextWithInterceptor(databaseName, "someone-else", CreatedAt))
            {
                context.OtpVerifications.Add(OtpVerification.Create(
                    Guid.NewGuid(),
                    "Purpose",
                    "+995555123456",
                    "otp-user-id",
                    "code-hash",
                    "request-hash",
                    entityChoseThis.AddMinutes(5),
                    3,
                    entityChoseThis));

                await context.SaveChangesAsync();
            }

            await using (var context = ContextWithInterceptor(databaseName, "someone-else", CreatedAt))
            {
                var persisted = await context.OtpVerifications.SingleAsync();

                Assert.That(persisted.Created, Is.EqualTo(entityChoseThis));
                Assert.That(persisted.CreatedBy, Is.EqualTo("otp-user-id"));
            }
        }

        /// <summary>
        /// Synchronous <c>SaveChanges</c> goes through a different interceptor method, so it needs
        /// its own case — stamping only the async path would leave sync writes unaudited.
        /// </summary>
        [Test]
        public void SynchronousSaveChanges_AlsoStamps()
        {
            var databaseName = TestDb.NewDatabaseName();

            using (var context = ContextWithInterceptor(databaseName, "sync-user", CreatedAt))
            {
                context.LoanApplications.Add(LoanApplication.Create(1, 250, 1, 6));

                context.SaveChanges();
            }

            using (var context = ContextWithInterceptor(databaseName, "sync-user", CreatedAt))
            {
                var persisted = context.LoanApplications.Single();

                Assert.That(persisted.Created, Is.EqualTo(CreatedAt));
                Assert.That(persisted.CreatedBy, Is.EqualTo("sync-user"));
            }
        }
    }
}
