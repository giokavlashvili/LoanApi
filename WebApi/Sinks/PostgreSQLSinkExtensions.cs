using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using WebApi.Sinks;

namespace WebApi.Sinks
{
    public static class PostgreSQLSinkExtensions
    {
        public static LoggerConfiguration PostgreSQL(
            this LoggerSinkConfiguration loggerConfiguration,
            string connectionString,
            LogEventLevel restrictedToMinimumLevel = LogEventLevel.Warning)
        {
            return loggerConfiguration.Sink(
                new PostgreSQLSink(connectionString),
                restrictedToMinimumLevel);
        }
    }
}

