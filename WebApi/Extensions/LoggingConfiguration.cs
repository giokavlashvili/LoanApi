using Application.Common.Logging;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Configurations;
using NpgsqlTypes;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using System.Data;
using WebApi.Logging;

// Aliased rather than importing Serilog.Sinks.PostgreSQL wholesale: that namespace also has a
// ColumnOptions, which would collide with the MSSqlServer one used below.
using PropertyWriteMethod = Serilog.Sinks.PostgreSQL.PropertyWriteMethod;

namespace WebApi.Extensions
{
    /// <summary>
    /// Composes the Serilog pipeline: enrichers, then level filtering, then sinks.
    /// <para>
    /// Levels come from the "Serilog" section of appsettings.json; everything structural
    /// lives here in code, where a typo is a compile error rather than a silently dead
    /// column. The database connection string is read straight from configuration — no
    /// global side channel is needed, unlike NLog's GlobalDiagnosticsContext.
    /// </para>
    /// <para>
    /// The database sink follows <c>Database:Provider</c> rather than carrying its own setting, so
    /// log rows always land in the same database EF migrated the Logs table into. Pointing the sink
    /// somewhere else would mean pointing it at a database where nothing creates that table.
    /// </para>
    /// </summary>
    public static class LoggingConfiguration
    {
        private const long FileSizeLimitBytes = 10 * 1024 * 1024;
        private const int RetainedFileCount = 14;

        public static void AddApplicationLogging(this WebApplicationBuilder builder)
        {
            EnableSelfLog();

            // Resolved via ReadFrom.Services below. Registered rather than constructed inline
            // so HttpContextEnricher gets IHttpContextAccessor from the container.
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<ILogEventEnricher, HttpContextEnricher>();
            builder.Services.AddSingleton<ILogEventEnricher, DefaultChannelEnricher>();

            var databaseProvider = builder.Configuration.GetDatabaseProvider();

            // Otherwise the framework's default Console/Debug providers stay registered
            // alongside Serilog and every line is emitted twice.
            builder.Logging.ClearProviders();

            builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .WriteTo.Console()
                .WriteTo.Async(sink => sink.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: RetainedFileCount,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u}|{CorrelationId}|{SourceContext}|{Message:lj} {Exception}|url: {Url}{NewLine}"))
                .WriteTo.Logger(databaseLogger => databaseLogger
                    .Filter.ByIncludingOnly(ShouldPersist)
                    .WriteTo.Database(builder.Configuration, databaseProvider)));
        }

        /// <summary>
        /// Attaches the database sink for the active provider. The <see cref="ShouldPersist"/> filter,
        /// the batch size and the batch period are deliberately the same on both, so switching
        /// provider does not quietly change which events get persisted or how long they sit in the
        /// queue.
        /// <para>
        /// The one setting that cannot be aligned is the queue bound.
        /// <c>MSSqlServerSinkOptions</c> exposes no queue limit, so the SQL Server sink takes
        /// Serilog's batching default; the PostgreSQL sink defaults to <c>int.MaxValue</c> instead,
        /// which is unbounded, so it is set explicitly below.
        /// </para>
        /// </summary>
        private static void Database(
            this LoggerSinkConfiguration sinkConfiguration,
            IConfiguration configuration,
            DatabaseProvider databaseProvider)
        {
            // Nothing to write to. Previously this branch still attached the SQL Server sink and
            // pointed it at DefaultConnection, so running with UseInMemoryDatabase true wrote log
            // rows to a real database the rest of the application was not using — or failed
            // repeatedly into SelfLog if that server was absent. Skipping it also matches
            // LogRetentionService, which is already not registered on this provider.
            if (databaseProvider == DatabaseProvider.InMemory)
                return;

            var connectionString = configuration.GetDatabaseConnectionString(databaseProvider);

            if (databaseProvider == DatabaseProvider.Postgres)
            {
                sinkConfiguration.PostgreSQL(
                    connectionString: connectionString,
                    tableName: LogConfiguration.TableName,
                    columnOptions: BuildPostgresColumnWriters(),
                    // Passed explicitly rather than left empty, because the sink builds
                    // "schema"."table" by concatenation. See DatabaseConfiguration.GetDefaultSchema
                    // for why this is the right value and what pins it.
                    schemaName: DatabaseConfiguration.GetDefaultSchema(DatabaseProvider.Postgres),
                    // EF owns this table, same as on SQL Server.
                    needAutoCreateTable: false,
                    needAutoCreateSchema: false,
                    // Off, though it is the package default. Binary COPY requires each declared
                    // NpgsqlDbType to line up with the real column type, and a mismatch fails the
                    // whole batch into SelfLog and nowhere else — the same silent-loss trap the
                    // column mapping below is commented for. Plain INSERTs tolerate the
                    // varchar/text distinctions here, and 50 rows a batch does not need COPY.
                    useCopy: false,
                    batchSizeLimit: 50,
                    period: TimeSpan.FromSeconds(5),
                    // This sink's default is int.MaxValue. Left alone, an unreachable database means
                    // events queue in memory until the process dies — the failure mode of a logging
                    // sink should be losing log rows, not taking the application with it.
                    queueLimit: 100_000);

                return;
            }

            sinkConfiguration.MSSqlServer(
                connectionString: connectionString,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = LogConfiguration.TableName,
                    // Stated rather than left to the sink's own "dbo" default, so both providers
                    // resolve the schema the same way and from the same place.
                    SchemaName = DatabaseConfiguration.GetDefaultSchema(DatabaseProvider.SqlServer),
                    // EF owns this schema (Infrastructure/Persistence/Configurations/
                    // LogConfiguration.cs). Letting the sink create it would produce a
                    // second, conflicting definition.
                    AutoCreateSqlTable = false,
                    BatchPostingLimit = 50,
                    BatchPeriod = TimeSpan.FromSeconds(5)
                },
                columnOptions: BuildColumnOptions());
        }

        /// <summary>
        /// Replaces NLog's <c>internalLogFile</c>. Serilog never throws out of a sink, so a
        /// column mapping that does not match the Logs table fails whole batches while the
        /// application carries on looking healthy — this file is the only place that surfaces
        /// it. Left on in every environment, as the NLog internal log was, because the failure
        /// this catches is one that production hits and Development does not.
        /// </summary>
        private static void EnableSelfLog()
        {
            try
            {
                var directory = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(directory);

                // SelfLog is written from sink threads, so the writer has to be synchronized.
                var writer = TextWriter.Synchronized(
                    new StreamWriter(
                        Path.Combine(directory, "serilog-selflog.txt"),
                        append: true) { AutoFlush = true });

                Serilog.Debugging.SelfLog.Enable(writer);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read-only or full disk must not stop the application from starting;
                // losing the diagnostic channel is bad, losing the app is worse.
                Serilog.Debugging.SelfLog.Enable(Console.Error);
            }
        }

        /// <summary>
        /// Reproduces nlog.config's routing in one predicate: the per-request row reaches the
        /// database at Information, everything else has to be Warning or worse. That is what
        /// keeps the table from filling with routine framework chatter.
        /// </summary>
        private static bool ShouldPersist(LogEvent logEvent)
        {
            if (logEvent.Level >= LogEventLevel.Warning)
                return true;

            return logEvent.Properties.TryGetValue(LogProperties.Channel, out var value)
                && value is ScalarValue { Value: string channel }
                && channel == LogProperties.Channels.Request;
        }

        /// <summary>
        /// Maps log event properties onto the existing Logs table.
        /// <para>
        /// This is the replacement for the hand written INSERT that NLog's database target
        /// used. Because AutoCreateSqlTable is off, the column set declared here must match
        /// the table exactly — a column named here that does not exist fails the whole batch,
        /// and the sink reports that only to SelfLog. Keep in sync with
        /// <see cref="LogProperties"/>, <c>Domain/Entities/Log.cs</c> and
        /// <c>LogConfiguration.cs</c>.
        /// </para>
        /// </summary>
        private static ColumnOptions BuildColumnOptions()
        {
            var options = new ColumnOptions();

            // The Logs table has none of these columns.
            options.Store.Remove(StandardColumn.Properties);
            options.Store.Remove(StandardColumn.MessageTemplate);
            options.Store.Remove(StandardColumn.LogEvent);
            options.Store.Remove(StandardColumn.TraceId);
            options.Store.Remove(StandardColumn.SpanId);

            // Id is a bigint IDENTITY — the server assigns it, the sink must not send it.
            options.Store.Remove(StandardColumn.Id);

            // Event time in UTC, not insert time: the sink batches, so the two differ.
            options.TimeStamp.ColumnName = nameof(Domain.Entities.Log.When);
            options.TimeStamp.ConvertToUtc = true;

            // nvarchar, not the tinyint that StoreAsEnum would write.
            options.Level.ColumnName = nameof(Domain.Entities.Log.Level);
            options.Level.StoreAsEnum = false;

            options.Message.ColumnName = nameof(Domain.Entities.Log.Message);
            options.Exception.ColumnName = nameof(Domain.Entities.Log.Exception);

            // Columns without an explicit PropertyName bind by matching name, which is why
            // LogProperties is worth having: the middleware's template holes land in columns
            // with no per-property configuration here.
            options.AdditionalColumns = new List<SqlColumn>
            {
                // Serilog's name for the logger is SourceContext, so this one needs the map.
                new()
                {
                    ColumnName = nameof(Domain.Entities.Log.Logger),
                    PropertyName = "SourceContext",
                    DataType = SqlDbType.NVarChar,
                    DataLength = 255,
                    AllowNull = true
                },
                Text(nameof(Domain.Entities.Log.CorrelationId), 64),
                Text(nameof(Domain.Entities.Log.Method), 10),
                Text(nameof(Domain.Entities.Log.Url), 2048),
                Number(nameof(Domain.Entities.Log.StatusCode)),
                Number(nameof(Domain.Entities.Log.DurationMs)),
                Text(nameof(Domain.Entities.Log.UserId), 128),
                Text(nameof(Domain.Entities.Log.UserName), 256),
                Text(nameof(Domain.Entities.Log.ClientIp), 64),
                Text(nameof(Domain.Entities.Log.MachineName), 128),
                Text(nameof(Domain.Entities.Log.RequestBody), -1),
                Text(nameof(Domain.Entities.Log.ResponseBody), -1),
                Text(nameof(Domain.Entities.Log.Channel), 20)
            };

            return options;
        }

        /// <summary>
        /// The PostgreSQL equivalent of <see cref="BuildColumnOptions"/>: one writer per column of
        /// the Logs table.
        /// <para>
        /// This sink takes the column set as a dictionary instead of a standard-columns-plus-extras
        /// object, so there is nothing to remove — a column simply is not listed. <c>Id</c> is
        /// absent for the same reason it is removed on the SQL Server side: it is a generated
        /// identity and the server assigns it.
        /// </para>
        /// <para>
        /// The keys are used as quoted identifiers, which is what allows the PascalCase column names
        /// to be shared with SQL Server — unquoted, PostgreSQL would fold them to lowercase and
        /// nothing would match. As with the SQL Server map, a key that is not a real column fails
        /// the whole batch and reports it only to SelfLog, so keep this in sync with
        /// <see cref="LogProperties"/>, <c>Domain/Entities/Log.cs</c> and <c>LogConfiguration.cs</c>.
        /// </para>
        /// </summary>
        private static IDictionary<string, ColumnWriterBase> BuildPostgresColumnWriters() =>
            new Dictionary<string, ColumnWriterBase>
            {
                // Event time, not insert time. timestamptz stores the instant, so unlike the
                // SQL Server side there is no ConvertToUtc equivalent to set — the offset carried
                // by the event is enough to place it.
                [nameof(Domain.Entities.Log.When)] = new TimestampColumnWriter(NpgsqlDbType.TimestampTz),

                // useAsText, so the column holds "Information" rather than the level's ordinal.
                // Matches StoreAsEnum = false on SQL Server.
                [nameof(Domain.Entities.Log.Level)] = new LevelColumnWriter(true, NpgsqlDbType.Varchar),

                [nameof(Domain.Entities.Log.Message)] = new RenderedMessageColumnWriter(NpgsqlDbType.Text),
                [nameof(Domain.Entities.Log.Exception)] = new ExceptionColumnWriter(NpgsqlDbType.Text),

                // Serilog's name for the logger is SourceContext, so this one needs the map.
                [nameof(Domain.Entities.Log.Logger)] = Property("SourceContext", NpgsqlDbType.Varchar),

                // The rest bind to identically named properties produced by the request logging
                // middleware. PropertyWriteMethod.Raw passes the scalar through as-is; ToString
                // would wrap every string in Serilog's quotes.
                [nameof(Domain.Entities.Log.CorrelationId)] = Property(nameof(Domain.Entities.Log.CorrelationId), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.Method)] = Property(nameof(Domain.Entities.Log.Method), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.Url)] = Property(nameof(Domain.Entities.Log.Url), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.StatusCode)] = Property(nameof(Domain.Entities.Log.StatusCode), NpgsqlDbType.Integer),
                [nameof(Domain.Entities.Log.DurationMs)] = Property(nameof(Domain.Entities.Log.DurationMs), NpgsqlDbType.Integer),
                [nameof(Domain.Entities.Log.UserId)] = Property(nameof(Domain.Entities.Log.UserId), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.UserName)] = Property(nameof(Domain.Entities.Log.UserName), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.ClientIp)] = Property(nameof(Domain.Entities.Log.ClientIp), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.MachineName)] = Property(nameof(Domain.Entities.Log.MachineName), NpgsqlDbType.Varchar),
                [nameof(Domain.Entities.Log.RequestBody)] = Property(nameof(Domain.Entities.Log.RequestBody), NpgsqlDbType.Text),
                [nameof(Domain.Entities.Log.ResponseBody)] = Property(nameof(Domain.Entities.Log.ResponseBody), NpgsqlDbType.Text),
                [nameof(Domain.Entities.Log.Channel)] = Property(nameof(Domain.Entities.Log.Channel), NpgsqlDbType.Varchar)
            };

        /// <summary>
        /// A column fed from a single log event property. Absent properties yield null, which every
        /// column mapped this way allows.
        /// </summary>
        private static SinglePropertyColumnWriter Property(string propertyName, NpgsqlDbType dbType) =>
            new(propertyName, PropertyWriteMethod.Raw, dbType);

        /// <summary>An nvarchar column; <paramref name="length"/> of -1 means MAX.</summary>
        private static SqlColumn Text(string name, int length) => new()
        {
            ColumnName = name,
            DataType = SqlDbType.NVarChar,
            DataLength = length,
            AllowNull = true
        };

        private static SqlColumn Number(string name) => new()
        {
            ColumnName = name,
            DataType = SqlDbType.Int,
            AllowNull = true
        };
    }
}
