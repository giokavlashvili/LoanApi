using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Infrastructure.UnitTests.Persistence
{
    /// <summary>
    /// Guards the one hazard created by declaring index filters in the shared configurations and
    /// re-declaring them for PostgreSQL: a filter added in one place and forgotten in the other.
    /// <para>
    /// EF passes a filter through to the DDL verbatim, so the mistake does not fail the build. On the
    /// two <c>UX_</c> indexes it would produce an unfiltered <em>unique</em> index — applying "one
    /// live OTP challenge per recipient" and "one active refresh token per session" to every row
    /// rather than only the live one, at which point ordinary inserts start failing. These tests turn
    /// that into a red test instead.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ProviderIndexFilterTests
    {
        /// <summary>
        /// Only the filters this codebase declares, which are all on <c>Domain.Entities</c> types.
        /// <para>
        /// ASP.NET Identity's tables are deliberately out of scope, because the two providers
        /// legitimately disagree there and the difference is not ours to reconcile: EF's SQL Server
        /// provider adds <c>[Column] IS NOT NULL</c> to a unique index over a nullable column, since
        /// SQL Server treats two NULLs as equal for uniqueness. PostgreSQL treats them as distinct,
        /// so it needs no such filter. That is why <c>UserNameIndex</c> and <c>RoleNameIndex</c> are
        /// filtered in the SQL Server snapshot and unfiltered in the PostgreSQL one.
        /// </para>
        /// </summary>
        private static Dictionary<string, string> FilteredIndexes(ApplicationDbContext context) =>
            context.Model.GetEntityTypes()
                .Where(entity => entity.ClrType.Namespace == "Domain.Entities")
                .SelectMany(entity => entity.GetIndexes())
                .Select(index => (Name: index.GetDatabaseName(), Filter: index.GetFilter()))
                .Where(x => !string.IsNullOrWhiteSpace(x.Filter))
                .ToDictionary(x => x.Name!, x => x.Filter!);

        private static Dictionary<string, string> SqlServerFilters()
        {
            using var context = TestDb.SqlServer();

            return FilteredIndexes(context);
        }

        private static Dictionary<string, string> PostgresFilters()
        {
            using var context = TestDb.Postgres();

            return FilteredIndexes(context);
        }

        [Test]
        public void EveryFilteredIndexExistsOnBothProviders()
        {
            Assert.That(
                PostgresFilters().Keys.OrderBy(k => k),
                Is.EqualTo(SqlServerFilters().Keys.OrderBy(k => k)),
                "A filtered index declared for one provider must be declared for the other. "
                + "Add the missing predicate to PostgresModelConfiguration.RequoteIndexFilters.");
        }

        [Test]
        public void PostgresFiltersQuoteIdentifiersInsteadOfBracketingThem()
        {
            var offenders = PostgresFilters()
                .Where(pair => pair.Value.Contains('['))
                .Select(pair => $"{pair.Key}: {pair.Value}")
                .ToList();

            // Brackets are array subscripting in PostgreSQL, not quoting, so a T-SQL predicate that
            // reaches it fails as a syntax error when the migration is applied.
            Assert.That(offenders, Is.Empty, "PostgreSQL index filters must use \"Column\", not [Column].");
        }

        [Test]
        public void SqlServerFiltersStillBracketIdentifiers()
        {
            var offenders = SqlServerFilters()
                .Where(pair => pair.Value.Contains('"'))
                .Select(pair => $"{pair.Key}: {pair.Value}")
                .ToList();

            // The mirror of the above: it would also catch the PostgreSQL pass leaking into the
            // SQL Server model, which would rewrite the snapshot the applied migrations share.
            Assert.That(offenders, Is.Empty, "SQL Server index filters must use [Column], not \"Column\".");
        }
    }
}
