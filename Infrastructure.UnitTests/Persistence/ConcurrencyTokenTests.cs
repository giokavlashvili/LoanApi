using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// Neither relational provider is exercised for real here — these assert against the built model,
    /// which is where the provider-specific configuration actually lands. True concurrency behaviour
    /// (a second SaveChanges throwing DbUpdateConcurrencyException) needs an integration test against
    /// a live server, which this repo does not have.
    /// <para>
    /// The two providers use different tokens: SQL Server has a <c>rowversion</c> column mapped from
    /// <c>RowVersion</c>, PostgreSQL leaves that property unmapped and uses the system <c>xmin</c>
    /// column instead. Both are asserted, because the whole point of splitting the configuration was
    /// that each provider ends up with exactly one working token and no leftovers from the other.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ConcurrencyTokenTests
    {
        private const string XminProperty = "xmin";

        private static IModel SqlServerModel()
        {
            using var context = TestDb.SqlServer();

            return context.Model;
        }

        private static IModel PostgresModel()
        {
            using var context = TestDb.Postgres();

            return context.Model;
        }

        [TestCase(typeof(LoanApplication))]
        [TestCase(typeof(OtpVerification))]
        [TestCase(typeof(RefreshToken))]
        public void SqlServer_UsesRowVersionAsTheConcurrencyToken(Type entityType)
        {
            var property = SqlServerModel().FindEntityType(entityType)!
                .FindProperty(nameof(LoanApplication.RowVersion));

            Assert.That(property, Is.Not.Null);
            Assert.That(property!.IsConcurrencyToken, Is.True);
            Assert.That(property.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));
        }

        [TestCase(typeof(LoanApplication))]
        [TestCase(typeof(OtpVerification))]
        [TestCase(typeof(RefreshToken))]
        public void Postgres_UsesXminAsTheConcurrencyToken(Type entityType)
        {
            var entity = PostgresModel().FindEntityType(entityType)!;

            var xmin = entity.FindProperty(XminProperty);

            Assert.That(xmin, Is.Not.Null, "xmin is the PostgreSQL concurrency token.");
            Assert.That(xmin!.IsConcurrencyToken, Is.True);
            Assert.That(xmin.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate));

            // Both halves matter. A mapped-but-unused RowVersion would be a dead bytea column, and
            // mapping it explicitly only to ignore it is what produced three EF warnings per start.
            Assert.That(
                entity.FindProperty(nameof(LoanApplication.RowVersion)),
                Is.Null,
                "RowVersion must not be mapped on PostgreSQL.");
        }

        [TestCase(typeof(Currency))]
        [TestCase(typeof(LoanType))]
        public void ReferenceData_HasNoConcurrencyTokenOnEitherProvider(Type entityType)
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    SqlServerModel().FindEntityType(entityType)!.FindProperty(nameof(LoanApplication.RowVersion)),
                    Is.Null);
                Assert.That(
                    PostgresModel().FindEntityType(entityType)!.FindProperty(nameof(LoanApplication.RowVersion)),
                    Is.Null);
                Assert.That(
                    PostgresModel().FindEntityType(entityType)!.FindProperty(XminProperty),
                    Is.Null);
            });
        }
    }
}
