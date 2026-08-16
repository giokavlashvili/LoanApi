using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    internal class LogConfiguration : IEntityTypeConfiguration<Log>
    {
        public void Configure(EntityTypeBuilder<Log> builder)
        {
            // 16, not 10: Serilog spells the levels out ("Information" is 11 characters),
            // where NLog wrote "Info". Too short and every request row fails to insert.
            builder.Property(l => l.Level).IsRequired().HasMaxLength(LogColumnLimits.Level);

            // Nullable: this maps to Serilog's SourceContext, which is absent on events
            // raised through the static Log.Logger (the bootstrap logger, the host-terminated
            // fatal in Program.cs). The sink batches and reports failures only to SelfLog,
            // so a NOT NULL violation here would drop whole batches silently.
            builder.Property(l => l.Logger).HasMaxLength(LogColumnLimits.Logger);

            builder.Property(l => l.Message).IsRequired();

            // Bounded so a long URL or user agent cannot push the row into LOB storage.
            builder.Property(l => l.CorrelationId).HasMaxLength(LogColumnLimits.CorrelationId);
            builder.Property(l => l.Method).HasMaxLength(LogColumnLimits.Method);
            builder.Property(l => l.Url).HasMaxLength(LogColumnLimits.Url);
            builder.Property(l => l.UserId).HasMaxLength(LogColumnLimits.UserId);
            builder.Property(l => l.UserName).HasMaxLength(LogColumnLimits.UserName);
            builder.Property(l => l.ClientIp).HasMaxLength(LogColumnLimits.ClientIp);
            builder.Property(l => l.MachineName).HasMaxLength(LogColumnLimits.MachineName);
            builder.Property(l => l.Channel).HasMaxLength(LogColumnLimits.Channel);

            // Retention purge and "what happened around <time>" both scan by time.
            builder.HasIndex(l => l.When)
                .HasDatabaseName("IX_Logs_When");

            // "Show me everything for this one request" — the primary troubleshooting path.
            builder.HasIndex(l => l.CorrelationId)
                .HasDatabaseName("IX_Logs_CorrelationId")
                .HasFilter("[CorrelationId] IS NOT NULL");

            // "All errors in the last day" without dragging the Request rows along.
            builder.HasIndex(l => new { l.Channel, l.Level, l.When })
                .HasDatabaseName("IX_Logs_Channel_Level_When");

            // Endpoint level triage: slowest or most-failing routes over a window.
            builder.HasIndex(l => new { l.StatusCode, l.When })
                .HasDatabaseName("IX_Logs_StatusCode_When")
                .HasFilter("[StatusCode] IS NOT NULL");
        }
    }
}
