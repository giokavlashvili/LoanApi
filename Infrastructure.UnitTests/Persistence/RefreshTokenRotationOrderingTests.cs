using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The unique filtered index on SessionId WHERE Status = 0 only backstops the concurrent refresh
    /// race if EF actually emits the UPDATE that consumes the presented token before the INSERT of
    /// its replacement within the same SaveChangesAsync batch. Same risk, and same reasoning, as
    /// <see cref="OtpReissueOrderingTests"/>: the in-memory provider ignores unique indexes
    /// entirely, so this can only be proven against real SQL Server. Requires
    /// localhost\SQLEXPRESS with the AddRefreshTokens migration applied; excluded from the normal
    /// run.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a real SQL Server instance; the in-memory provider ignores unique indexes.")]
    public class RefreshTokenRotationOrderingTests
    {
        private const string UserId = "rotation-ordering-test-user";

        private static ApplicationDbContext CreateContext() => TestDb.SqlServer();

        [Test]
        public async Task RotatingATokenInOneSave_ConsumesThenInsertsWithoutTrippingTheActiveIndex()
        {
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            await using var context = CreateContext();

            var original = RefreshToken.Create(
                sessionId, UserId, $"ordering-hash-{sessionId:N}-1",
                expiresAt: now.AddDays(14), sessionExpiresAt: now.AddDays(30), created: now);

            context.RefreshTokens.Add(original);
            await context.SaveChangesAsync(default);

            try
            {
                // Act — exactly what RefreshTokenService.RotateAsync does: consume the presented
                // token and add its replacement, both in a single SaveChangesAsync.
                original.Consume(now);

                var replacement = RefreshToken.Create(
                    sessionId, UserId, $"ordering-hash-{sessionId:N}-2",
                    expiresAt: now.AddDays(14), sessionExpiresAt: original.SessionExpiresAt, created: now);

                context.RefreshTokens.Add(replacement);

                // Assert — must not throw. If EF ever emits the INSERT before the UPDATE, this
                // fails with DbUpdateException on the unique filtered index, because for a moment
                // two Active rows would exist for the same session.
                Assert.DoesNotThrowAsync(async () => await context.SaveChangesAsync(default));

                Assert.That(original.Status, Is.EqualTo(RefreshTokenStatus.Consumed));
                Assert.That(replacement.Status, Is.EqualTo(RefreshTokenStatus.Active));
            }
            finally
            {
                context.RefreshTokens.RemoveRange(
                    context.RefreshTokens.Where(r => r.SessionId == sessionId));
                await context.SaveChangesAsync(default);
            }
        }
    }
}
