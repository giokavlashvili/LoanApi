using Application.Common.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using System.Data;
using System.Reflection;
using System.Text;
using WebApi.Logging;

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
    /// </summary>
    public static class LoggingConfiguration
    {
        private const long FileSizeLimitBytes = 10 * 1024 * 1024;
        private const int RetainedFileCount = 14;

        public static void AddApplicationLogging(this WebApplicationBuilder builder)
        {
            var logDirectory = ResolveLogDirectory(builder.Configuration);

            EnableSelfLog(logDirectory);

            // Resolved via ReadFrom.Services below. Registered rather than constructed inline
            // so HttpContextEnricher gets IHttpContextAccessor from the container.
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<ILogEventEnricher, HttpContextEnricher>();
            builder.Services.AddSingleton<ILogEventEnricher, DefaultChannelEnricher>();

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

            // Otherwise the framework's default Console/Debug providers stay registered
            // alongside Serilog and every line is emitted twice.
            builder.Logging.ClearProviders();

            builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                // Which deployment and which build produced the row. Without these, logs
                // aggregated from more than one environment cannot be told apart, and a
                // regression cannot be attributed to the release that introduced it. Neither
                // is a column in the Logs table — they reach the console and file sinks, and
                // any structured sink added later.
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
                .Enrich.WithProperty("Version", ResolveApplicationVersion())
                // Async, like the file sink below. The console sink holds a lock for the
                // duration of each write, so on a busy server an unwrapped one serialises
                // request threads against stdout — which is worse in a container, where
                // stdout is a pipe to the runtime rather than a terminal.
                .WriteTo.Async(sink => sink.Console())
                .WriteTo.Async(sink => sink.File(
                    path: Path.Combine(logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: RetainedFileCount,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}|{Level:u}|{CorrelationId}|{SourceContext}|{Message:lj} {Exception}|url: {Url}{NewLine}"))
                .WriteTo.Logger(databaseLogger => databaseLogger
                    .Filter.ByIncludingOnly(ShouldPersist)
                    .WriteTo.MSSqlServer(
                        connectionString: connectionString,
                        sinkOptions: new MSSqlServerSinkOptions
                        {
                            TableName = "Logs",
                            // EF owns this schema (Infrastructure/Persistence/Configurations/
                            // LogConfiguration.cs). Letting the sink create it would produce a
                            // second, conflicting definition.
                            AutoCreateSqlTable = false,
                            BatchPostingLimit = 50,
                            BatchPeriod = TimeSpan.FromSeconds(5)
                        },
                        columnOptions: BuildColumnOptions())));
        }

        /// <summary>
        /// Where the file sink and the SelfLog are written. Defaults to a <c>logs</c> folder
        /// beside the binaries, which is fine on a developer machine and wrong nearly
        /// everywhere else: in a container that path is on the ephemeral write layer and is
        /// lost with the instance, and under a read-only root filesystem it cannot be created
        /// at all. <c>Serilog:LogDirectory</c> points it at a mounted volume without a rebuild.
        /// </summary>
        private static string ResolveLogDirectory(IConfiguration configuration)
        {
            var configured = configuration["Serilog:LogDirectory"];

            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "logs")
                : configured;
        }

        /// <summary>
        /// Informational version when the build stamps one (it carries the git hash under most
        /// CI setups), falling back to the assembly version.
        /// </summary>
        private static string ResolveApplicationVersion()
        {
            var assembly = Assembly.GetEntryAssembly();

            if (assembly is null)
                return "unknown";

            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }

        /// <summary>
        /// Replaces NLog's <c>internalLogFile</c>. Serilog never throws out of a sink, so a
        /// column mapping that does not match the Logs table fails whole batches while the
        /// application carries on looking healthy — this file is the only place that surfaces
        /// it. Left on in every environment, as the NLog internal log was, because the failure
        /// this catches is one that production hits and Development does not.
        /// </summary>
        private static void EnableSelfLog(string directory)
        {
            try
            {
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

            ValidateAgainstLogEntity(options);

            return options;
        }

        /// <summary>
        /// Fails startup when the sink's column map and the <c>Log</c> entity disagree.
        /// <para>
        /// One table has three definitions that must stay aligned — <c>Domain/Entities/Log.cs</c>,
        /// <c>LogConfiguration.cs</c> and the map above — and until now nothing checked. Because
        /// <c>AutoCreateSqlTable</c> is off, naming a column the table does not have fails the
        /// <em>entire batch</em>, and the sink reports that to <c>SelfLog</c> alone: the
        /// application stays up, serves traffic, and quietly stops recording anything. That is
        /// the worst failure mode in the codebase, and it is the one with no signal.
        /// </para>
        /// <para>
        /// The reverse direction is checked too. A property added to <c>Log</c> and its
        /// migration but not to the map inserts as NULL forever, which reads as "the feature is
        /// broken" rather than "a line is missing here".
        /// </para>
        /// <para>
        /// This validates the map against the entity, not against the live table — it cannot
        /// catch a migration that was never applied. It catches the drift that happens while
        /// editing code, which is the drift that actually happens.
        /// </para>
        /// </summary>
        private static void ValidateAgainstLogEntity(ColumnOptions options)
        {
            var mapped = new HashSet<string>(StringComparer.Ordinal)
            {
                options.TimeStamp.ColumnName,
                options.Level.ColumnName,
                options.Message.ColumnName,
                options.Exception.ColumnName
            };

            foreach (var column in options.AdditionalColumns ?? Enumerable.Empty<SqlColumn>())
                mapped.Add(column.ColumnName);

            // Settable only: a computed getter-only property is not a column the sink could
            // fill, so flagging it would be a false alarm that blocks startup.
            var entityProperties = typeof(Domain.Entities.Log)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanWrite)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            // Id is a bigint IDENTITY the server assigns, so the sink deliberately never sends it.
            entityProperties.Remove(nameof(Domain.Entities.Log.Id));

            var unknownColumns = mapped.Except(entityProperties).Order().ToList();
            var unmappedProperties = entityProperties.Except(mapped).Order().ToList();

            if (unknownColumns.Count == 0 && unmappedProperties.Count == 0)
                return;

            var message = new StringBuilder(
                "The Serilog column map and the Log entity have drifted. Reconcile " +
                "WebApi/Extensions/LoggingConfiguration.BuildColumnOptions, Domain/Entities/Log.cs and " +
                "Infrastructure/Persistence/Configurations/LogConfiguration.cs.");

            if (unknownColumns.Count > 0)
            {
                message.Append(" Columns written by the sink with no matching property on Log (these fail " +
                    "every insert batch): ").Append(string.Join(", ", unknownColumns)).Append('.');
            }

            if (unmappedProperties.Count > 0)
            {
                message.Append(" Properties on Log the sink never populates (these stay NULL): ")
                    .Append(string.Join(", ", unmappedProperties)).Append('.');
            }

            throw new InvalidOperationException(message.ToString());
        }

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
