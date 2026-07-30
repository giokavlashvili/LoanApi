using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// Pins the one place the two providers genuinely behave differently rather than merely mapping
    /// differently: a detached <c>Update</c>/<c>Remove</c> cannot work when the concurrency token is
    /// PostgreSQL's <c>xmin</c>, because a shadow property's value does not survive detachment.
    /// <para>
    /// Verified against a real PostgreSQL 18 instance before the guard existed: the save threw
    /// <c>DbUpdateConcurrencyException</c> ("expected to affect 1 row(s), but actually affected 0"),
    /// which reads as a lost update race when nothing had touched the row. These tests assert the
    /// failure now happens at the call site with an explanation instead.
    /// </para>
    /// <para>
    /// No database is touched here — building the model is enough, and neither <c>Update</c> nor
    /// <c>Remove</c> opens a connection.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DetachedWriteGuardTests
    {
        private static OtpVerification Detached() => new();

        [Test]
        public void Postgres_RejectsADetachedUpdate()
        {
            using var context = TestDb.Postgres();
            var repository = new Repository<OtpVerification>(context);

            var exception = Assert.Throws<InvalidOperationException>(
                () => repository.Update(Detached()));

            Assert.That(exception!.Message, Does.Contain("xmin"));
            Assert.That(exception.Message, Does.Contain("shadow property"));
        }

        [Test]
        public void Postgres_RejectsADetachedRemove()
        {
            using var context = TestDb.Postgres();
            var repository = new Repository<OtpVerification>(context);

            Assert.Throws<InvalidOperationException>(() => repository.Remove(Detached()));
        }

        [Test]
        public void SqlServer_AllowsADetachedUpdate()
        {
            using var context = TestDb.SqlServer();
            var repository = new Repository<OtpVerification>(context);

            // RowVersion is a real property, so it travels with the detached instance and the
            // concurrency check still has a value to compare. Nothing to guard against.
            Assert.DoesNotThrow(() => repository.Update(Detached()));
        }

        [Test]
        public void InMemory_AllowsADetachedUpdate()
        {
            using var context = TestDb.InMemory();
            var repository = new Repository<OtpVerification>(context);

            // No concurrency token at all on this provider, so nothing can be lost by detaching.
            Assert.DoesNotThrow(() => repository.Update(Detached()));
        }
    }
}
