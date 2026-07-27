using Application.Common.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using System.Data;
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
            EnableSelfLog();

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
