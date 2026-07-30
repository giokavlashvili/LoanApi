namespace Domain.Common
{
    /// <summary>
    /// Marks an entity that carries a <see cref="RowVersion"/> optimistic-concurrency token, so the
    /// provider-specific model configuration can reach the property through a lambda instead of the
    /// string <c>"RowVersion"</c>.
    /// <para>
    /// That is the whole reason this exists. <c>EntityTypeBuilder.Ignore(string)</c> does not validate
    /// the name, so on PostgreSQL — where the property has to be ignored in favour of the system
    /// <c>xmin</c> column — a rename would silently stop ignoring it and put a dead <c>bytea</c>
    /// column in the next migration. With this constraint the same mistake is a compile error.
    /// </para>
    /// <para>
    /// Deliberately not folded into <see cref="BaseAuditableEntity"/>, even though the three entities
    /// implementing it happen to be exactly the auditable ones today: "has an audit trail" and "is
    /// guarded against concurrent writes" are separate decisions, and an aggregate could reasonably
    /// want one without the other.
    /// </para>
    /// </summary>
    public interface IHasRowVersion
    {
        /// <summary>
        /// The token itself. Mapped to a SQL Server <c>rowversion</c> column; <b>unmapped on
        /// PostgreSQL</b>, which uses <c>xmin</c> instead and leaves this permanently
        /// <see cref="System.Array.Empty{T}"/>. See
        /// <c>Infrastructure/Persistence/Configurations/Providers/</c>.
        /// </summary>
        byte[] RowVersion { get; }
    }
}
