using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The unique filtered index on (Recipient, Purpose) WHERE Status = 0 only backstops the OTP
    /// throttle race if EF actually emits the UPDATE that expires the old challenge before the
    /// INSERT of the new one within the same SaveChangesAsync batch. The in-memory provider
    /// ignores unique indexes entirely, so this can only be proven against real SQL Server — see
    /// docs/plans/02-correctness-fixes.md task 5, step 2. Requires localhost\SQLEXPRESS with the
    /// AddConcurrencyTokensAndOtpUniqueIndex migration applied; excluded from the normal run.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a real SQL Server instance; the in-memory provider ignores unique indexes.")]
    public class OtpReissueOrderingTests
    {
        private static ApplicationDbContext CreateContext() => TestDb.SqlServer();

        [Test]
        public async Task ReissuingForTheSameRecipientAndPurpose_InvalidatesThenInsertsInOneSave()
        {
            const string recipient = "+995500000001";
            const string purpose = "ConcurrencyOrderingTest";
            var now = DateTime.UtcNow;

            await using var context = CreateContext();

            var original = OtpVerification.Create(
                Guid.NewGuid(), purpose, recipient, VerificationChannel.Sms, userId: null,
                codeHash: "hash-1", requestHash: "request-hash",
                expiresAt: now.AddMinutes(5), maxAttempts: 3, created: now);

            context.OtpVerifications.Add(original);
            await context.SaveChangesAsync(default);

            try
            {
                // Act — exactly what OtpService.IssueAsync does on a reissue: invalidate the
                // previous pending challenge and add a new one, both in a single SaveChangesAsync.
                original.Invalidate(now);

                var reissued = OtpVerification.Create(
                    Guid.NewGuid(), purpose, recipient, VerificationChannel.Sms, userId: null,
                    codeHash: "hash-2", requestHash: "request-hash",
                    expiresAt: now.AddMinutes(5), maxAttempts: 3, created: now);

                context.OtpVerifications.Add(reissued);

                // Assert — must not throw. If EF ever emits the INSERT before the UPDATE, this
                // fails with DbUpdateException on the unique filtered index, because for a
                // moment two Pending rows would exist for the same recipient + purpose.
                Assert.DoesNotThrowAsync(async () => await context.SaveChangesAsync(default));

                Assert.That(original.Status, Is.EqualTo(OtpVerificationStatus.Expired));
                Assert.That(reissued.Status, Is.EqualTo(OtpVerificationStatus.Pending));
            }
            finally
            {
                context.OtpVerifications.RemoveRange(
                    context.OtpVerifications.Where(o => o.Recipient == recipient && o.Purpose == purpose));
                await context.SaveChangesAsync(default);
            }
        }
    }
}
