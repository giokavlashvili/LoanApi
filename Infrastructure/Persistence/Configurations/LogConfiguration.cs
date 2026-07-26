using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    internal class LogConfiguration : IEntityTypeConfiguration<Log>
    {
        public void Configure(EntityTypeBuilder<Log> builder)
        {
            builder.Property(l => l.Level).IsRequired().HasMaxLength(10);
            builder.Property(l => l.Logger).IsRequired().HasMaxLength(255);
            builder.Property(l => l.Message).IsRequired();

            // Bounded so a long URL or user agent cannot push the row into LOB storage.
            builder.Property(l => l.CorrelationId).HasMaxLength(64);
            builder.Property(l => l.Method).HasMaxLength(10);
            builder.Property(l => l.Url).HasMaxLength(2048);
            builder.Property(l => l.UserId).HasMaxLength(128);
            builder.Property(l => l.UserName).HasMaxLength(256);
            builder.Property(l => l.ClientIp).HasMaxLength(64);
            builder.Property(l => l.MachineName).HasMaxLength(128);
            builder.Property(l => l.Channel).HasMaxLength(20);

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
