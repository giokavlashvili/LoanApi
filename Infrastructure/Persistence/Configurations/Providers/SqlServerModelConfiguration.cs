using Domain.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Configurations.Providers
{
    /// <summary>
    /// The SQL Server half of the concurrency-token configuration. Its counterpart is
    /// <see cref="PostgresModelConfiguration"/>.
    /// <para>
    /// This exists because <c>IsRowVersion()</c> cannot live in the shared configurations. Left
    /// there, PostgreSQL had to map the property and then ignore it, and EF reports that as
    /// "<c>The property 'X.RowVersion' was first mapped explicitly and then ignored</c>" — three
    /// warnings at every start, at <c>Warning</c> level, which <c>ShouldPersist</c> then writes into
    /// the Logs table on every single boot. Declaring it per provider means neither provider
    /// configures a property the other one throws away.
    /// </para>
    /// <para>
    /// The resulting SQL Server model is identical to the one the shared configurations produced, so
    /// <c>ApplicationDbContextModelSnapshot</c> and the applied migrations are unaffected.
    /// </para>
    /// </summary>
    internal static class SqlServerModelConfiguration
    {
        internal static void Apply(ModelBuilder builder)
        {
            // Index filters are not repeated here: the shared configurations already spell them in
            // T-SQL, which is the dialect this provider wants.
            UseRowVersionAsConcurrencyToken<LoanApplication>(builder);
            UseRowVersionAsConcurrencyToken<OtpVerification>(builder);
            UseRowVersionAsConcurrencyToken<RefreshToken>(builder);
        }

        /// <summary>
        /// Maps <c>RowVersion</c> to a SQL Server <c>rowversion</c> column, which the server bumps on
        /// every update to the row.
        /// </summary>
        /// <remarks>
        /// The <see cref="IHasRowVersion"/> constraint is what allows the lambda; see that interface
        /// for why the string form was not good enough.
        /// </remarks>
        private static void UseRowVersionAsConcurrencyToken<TEntity>(ModelBuilder builder)
            where TEntity : class, IHasRowVersion =>
            builder.Entity<TEntity>()
                .Property(entity => entity.RowVersion)
                .IsRowVersion();
    }
}
