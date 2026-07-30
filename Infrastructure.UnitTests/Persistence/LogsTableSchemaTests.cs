using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// The Serilog database sinks are configured outside EF and take the table and schema as plain
    /// arguments, so nothing links them to the model automatically. These tests supply that link.
    /// <para>
    /// A mismatch is the worst kind of failure this codebase has: the sink batches, an insert against
    /// a table that is not there fails the whole batch, and the only report is
    /// <c>logs/serilog-selflog.txt</c>. The application carries on looking healthy while the Logs
    /// table silently stops filling.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LogsTableSchemaTests
    {
        [Test]
        public void TableNameConstantMatchesTheMappedTable()
        {
            using var sqlServer = TestDb.SqlServer();
            using var postgres = TestDb.Postgres();

            Assert.Multiple(() =>
            {
                Assert.That(
                    sqlServer.Model.FindEntityType(typeof(Log))!.GetTableName(),
                    Is.EqualTo(LogConfiguration.TableName));
                Assert.That(
                    postgres.Model.FindEntityType(typeof(Log))!.GetTableName(),
                    Is.EqualTo(LogConfiguration.TableName));
            });
        }

        [TestCase(DatabaseProvider.SqlServer)]
        [TestCase(DatabaseProvider.Postgres)]
        public void LogsUsesTheProviderDefaultSchema(DatabaseProvider provider)
        {
            using var context = provider == DatabaseProvider.SqlServer
                ? TestDb.SqlServer()
                : TestDb.Postgres();

            var entityType = context.Model.FindEntityType(typeof(Log))!;

            // A null schema means "whatever the provider's default is", which is exactly the
            // assumption GetDefaultSchema encodes. The moment anyone calls HasDefaultSchema or passes
            // a schema to ToTable, this stops being null and the hardcoded value in the sinks becomes
            // wrong — which is the point of asserting it here rather than trusting the comment.
            Assert.That(
                entityType.GetSchema(),
                Is.Null,
                $"Logs now has an explicit schema. Update DatabaseConfiguration.GetDefaultSchema and "
                + $"the {provider} sink in LoggingConfiguration to match.");

            Assert.That(DatabaseConfiguration.GetDefaultSchema(provider), Is.Not.Empty);
        }

        [Test]
        public void InMemoryHasNoSchema()
        {
            // Guards the throw rather than the value: the in-memory provider is not relational, and a
            // caller reaching for its schema has made a mistake worth surfacing.
            Assert.Throws<InvalidOperationException>(
                () => DatabaseConfiguration.GetDefaultSchema(DatabaseProvider.InMemory));
        }
    }
}
