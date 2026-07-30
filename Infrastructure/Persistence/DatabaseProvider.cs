namespace Infrastructure.Persistence
{
    /// <summary>
    /// Which store backs <see cref="ApplicationDbContext"/> for this process. Selected once at
    /// startup from configuration and never switched at runtime — the model itself differs per
    /// provider (see <c>Configurations/Providers/PostgresModelConfiguration.cs</c>), so a single
    /// process cannot serve two of these.
    /// </summary>
    public enum DatabaseProvider
    {
        /// <summary>The default. Migrations live in <c>Infrastructure/Migrations</c>.</summary>
        SqlServer,

        /// <summary>Migrations live in the separate <c>Infrastructure.Postgres</c> assembly.</summary>
        Postgres,

        /// <summary>
        /// Tests and throwaway runs only. Not a deployment target: the filtered unique indexes that
        /// enforce "one live OTP challenge" and "one live refresh token per session" do not exist on
        /// a non-relational provider, so those invariants are unenforced here.
        /// </summary>
        InMemory
    }
}
