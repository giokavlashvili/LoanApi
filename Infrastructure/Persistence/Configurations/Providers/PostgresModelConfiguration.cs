using Domain.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Configurations.Providers
{
    /// <summary>
    /// The parts of the model PostgreSQL cannot take as written, applied as a second pass over the
    /// shared <see cref="IEntityTypeConfiguration{TEntity}"/> classes. Its counterpart is
    /// <see cref="SqlServerModelConfiguration"/>.
    /// <para>
    /// <b>Why a second pass and not provider-aware configurations.</b> The configuration classes are
    /// discovered by <c>ApplyConfigurationsFromAssembly</c>, which requires a parameterless
    /// constructor, so nothing can be injected into them — and the alternative of stashing the
    /// provider in a model annotation would emit that annotation into
    /// <c>ApplicationDbContextModelSnapshot</c> and churn the SQL Server migration history for a
    /// value that is not part of the schema. Overriding afterwards keeps the SQL Server model
    /// byte-identical to what it was before PostgreSQL existed, which is the property worth
    /// protecting: the seven existing migrations and the snapshot are untouched.
    /// </para>
    /// <para>
    /// The index filters are the one part that has to be duplicated rather than split, because they
    /// are declared alongside the indexes themselves in the shared configurations — in T-SQL. Every
    /// one of them has to be repeated here. A filter added there and forgotten here does not fail
    /// the build; it silently produces an unfiltered index on PostgreSQL, which for the two
    /// <c>UX_</c> indexes below would mean the uniqueness rule applies to <em>every</em> row rather
    /// than only the live one, and inserts would start failing. <c>ProviderIndexFilterTests</c>
    /// exists to turn that into a failing test, but keep <see cref="Apply"/> complete anyway.
    /// </para>
    /// </summary>
    internal static class PostgresModelConfiguration
    {
        internal static void Apply(ModelBuilder builder)
        {
            RequoteIndexFilters(builder);

            // Every aggregate carrying a RowVersion. See the method for why the property cannot
            // simply be reused.
            UseXminAsConcurrencyToken<LoanApplication>(builder);
            UseXminAsConcurrencyToken<OtpVerification>(builder);
            UseXminAsConcurrencyToken<RefreshToken>(builder);
        }

        /// <summary>
        /// Restates the four filtered indexes with double-quoted identifiers. EF passes a filter
        /// through to the DDL verbatim, so <c>[Status] = 0</c> reaches PostgreSQL unchanged and
        /// fails as a syntax error at migration time — brackets are array subscripting there, not
        /// quoting.
        /// </summary>
        /// <remarks>
        /// Re-calling <c>HasIndex</c> with the same properties returns the builder for the index the
        /// shared configuration already declared rather than adding a second one, so these only
        /// replace the filter. The <c>RefreshToken</c> line has to pass the model name for the same
        /// reason <c>RefreshTokenConfiguration</c> does: two indexes cover <c>SessionId</c>, and the
        /// name is the only thing that distinguishes the unique live-token one from the plain
        /// lineage lookup.
        /// </remarks>
        private static void RequoteIndexFilters(ModelBuilder builder)
        {
            builder.Entity<Log>()
                .HasIndex(l => l.CorrelationId)
                .HasFilter("\"CorrelationId\" IS NOT NULL");

            builder.Entity<Log>()
                .HasIndex(l => new { l.StatusCode, l.When })
                .HasFilter("\"StatusCode\" IS NOT NULL");

            // 0 is OtpVerificationStatus.Pending.
            builder.Entity<OtpVerification>()
                .HasIndex(o => new { o.Recipient, o.Purpose })
                .HasFilter("\"Status\" = 0");

            // 0 is RefreshTokenStatus.Active.
            builder.Entity<RefreshToken>()
                .HasIndex(r => r.SessionId, "UX_RefreshTokens_SessionId_Active")
                .HasFilter("\"Status\" = 0");
        }

        /// <summary>
        /// Swaps the <c>byte[] RowVersion</c> token for PostgreSQL's <c>xmin</c> system column, so
        /// optimistic concurrency keeps working without the Domain knowing which provider is in use.
        /// <para>
        /// PostgreSQL has no <c>rowversion</c> type and nothing that auto-increments a
        /// <c>bytea</c> on update, so <c>IsRowVersion()</c> would map a column that never changes —
        /// leaving every concurrent write to succeed and silently overwrite, which is the exact
        /// failure the token exists to prevent. <c>xmin</c> is the transaction id that last wrote
        /// the row; the server bumps it on every update for free.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Npgsql 10 dropped the <c>UseXminAsConcurrencyToken()</c> shorthand, but the convention
        /// behind it is still there: a <c>uint</c> property that is a concurrency token and
        /// <c>ValueGeneratedOnAddOrUpdate</c> is mapped to the system column. The column name
        /// matters — <c>NpgsqlMigrationsSqlGenerator.SystemColumnNames</c> is what keeps <c>xmin</c>
        /// out of the generated migration, since it is not a column anyone creates.
        /// <para>
        /// <c>RowVersion</c> is ignored rather than kept as a dead <c>bytea</c> column. It stays on
        /// the entity, always <c>Array.Empty&lt;byte&gt;()</c>, so the note in
        /// <c>Repository.Update</c> about <c>Update</c> churning the token describes SQL Server
        /// only. Nothing reads the property, so no caller is affected. The <c>Ignore</c> is still
        /// needed even though no configuration maps the property — EF's default convention would
        /// otherwise pick up the public <c>byte[]</c> and map it.
        /// </para>
        /// <para>
        /// The <see cref="IHasRowVersion"/> constraint exists so the <c>Ignore</c> below is a lambda.
        /// <c>Ignore(string)</c> silently accepts a name that no longer exists, which would leave the
        /// property mapped and add a <c>bytea</c> column to the next migration with nothing
        /// complaining.
        /// </para>
        /// </remarks>
        private static void UseXminAsConcurrencyToken<TEntity>(ModelBuilder builder)
            where TEntity : class, IHasRowVersion
        {
            var entity = builder.Entity<TEntity>();

            entity.Ignore(e => e.RowVersion);

            entity.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate();
        }
    }
}
