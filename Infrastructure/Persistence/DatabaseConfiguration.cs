using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence
{
    /// <summary>
    /// The single place that answers "which database is this process talking to, and over which
    /// connection string". Both the EF registration in <c>AddInfrastructureServices</c> and the
    /// Serilog database sink in <c>WebApi/Extensions/LoggingConfiguration.cs</c> resolve through
    /// here, which is what keeps log rows landing in the same database as the application data
    /// rather than in whichever one each side happened to read.
    /// </summary>
    public static class DatabaseConfiguration
    {
        public const string SectionName = "Database";

        /// <summary>Connection string name for <see cref="DatabaseProvider.SqlServer"/>.</summary>
        public const string SqlServerConnectionName = "DefaultConnection";

        /// <summary>Connection string name for <see cref="DatabaseProvider.Postgres"/>.</summary>
        public const string PostgresConnectionName = "PostgresConnection";

        /// <summary>
        /// The migrations assembly for PostgreSQL. A string rather than a <c>typeof(...).Assembly</c>
        /// because <c>Infrastructure</c> deliberately does not reference
        /// <c>Infrastructure.Postgres</c> — the dependency runs the other way. <c>WebApi</c>
        /// references it so the assembly is actually loaded at runtime.
        /// </summary>
        public const string PostgresMigrationsAssembly = "Infrastructure.Postgres";

        /// <summary>
        /// Resolves <c>Database:Provider</c>. Accepts the spellings people actually write
        /// ("Postgres", "PostgreSQL", "Npgsql") because a typo here otherwise surfaces as the
        /// application silently starting on SQL Server.
        /// </summary>
        public static DatabaseProvider GetDatabaseProvider(this IConfiguration configuration)
        {
            // The original switch, still honoured and still winning. Removing it would break every
            // existing appsettings file and the docs that describe it; treating it as an override
            // means "UseInMemoryDatabase": true keeps doing exactly what it did before this setting
            // existed.
            if (configuration.GetValue<bool>("UseInMemoryDatabase"))
                return DatabaseProvider.InMemory;

            var configured = configuration.GetValue<string?>($"{SectionName}:Provider");

            // Unset is SqlServer, which is what every appsettings file in this repo predates the
            // setting with.
            if (string.IsNullOrWhiteSpace(configured))
                return DatabaseProvider.SqlServer;

            return configured.Trim().ToLowerInvariant() switch
            {
                "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
                "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
                "inmemory" => DatabaseProvider.InMemory,
                _ => throw new InvalidOperationException(
                    $"Unknown {SectionName}:Provider value '{configured}'. "
                    + "Valid values are SqlServer, Postgres and InMemory.")
            };
        }

        /// <summary>
        /// The connection string for <paramref name="provider"/>, or a throw naming the missing key.
        /// Returning null here is what the <c>#pragma warning disable CS8604</c> in
        /// <c>AddInfrastructureServices</c> used to be suppressing; failing at startup with the key
        /// name beats handing a null to the provider and reading whatever it makes of it.
        /// <para>
        /// Empty counts as missing, deliberately. <c>PostgresConnection</c> ships as <c>""</c> in
        /// <c>appsettings.json</c> because a PostgreSQL connection string has to carry credentials and
        /// those do not belong in a tracked file — the same treatment <c>JWT:Secret</c> and
        /// <c>Otp:Secret</c> get, and for the same reason. Development supplies a local value in
        /// <c>appsettings.Development.json</c>, so clone-and-run still works; anything else has to
        /// provide one, and finds out at boot rather than on the first query.
        /// </para>
        /// </summary>
        public static string GetDatabaseConnectionString(
            this IConfiguration configuration,
            DatabaseProvider provider)
        {
            var name = GetConnectionStringName(provider);
            var connectionString = configuration.GetConnectionString(name);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"No connection string named '{name}' is configured, and "
                    + $"{SectionName}:Provider is set to {provider}. Supply one via configuration or "
                    + $"user secrets: dotnet user-secrets set \"ConnectionStrings:{name}\" \"...\" "
                    + "--project WebApi");
            }

            return connectionString;
        }

        /// <summary>
        /// The schema the <c>Logs</c> table lives in, needed by the Serilog database sinks because
        /// both take the schema as an explicit argument.
        /// <para>
        /// These are the two providers' own default schemas, which is where EF puts every table in
        /// this model: nothing calls <c>HasDefaultSchema</c>, and no configuration passes a schema to
        /// <c>ToTable</c>. That is an assumption rather than something derived, so
        /// <c>LogsTableSchemaTests</c> asserts the model really does leave the schema unset — set one
        /// anywhere and that test fails rather than the sink quietly inserting into a table that is
        /// not there.
        /// </para>
        /// </summary>
        public static string GetDefaultSchema(DatabaseProvider provider) => provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.Postgres => "public",
            DatabaseProvider.InMemory => throw new InvalidOperationException(
                "The in-memory provider has no schema."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

        /// <summary>
        /// Which <c>ConnectionStrings</c> entry backs <paramref name="provider"/>. Separate names
        /// rather than one reinterpreted <c>DefaultConnection</c> so both can sit in appsettings at
        /// once and switching provider is a one-line change.
        /// </summary>
        public static string GetConnectionStringName(DatabaseProvider provider) => provider switch
        {
            DatabaseProvider.SqlServer => SqlServerConnectionName,
            DatabaseProvider.Postgres => PostgresConnectionName,
            DatabaseProvider.InMemory => throw new InvalidOperationException(
                "The in-memory provider has no connection string."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }
}
