using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    internal class LogConfiguration : IEntityTypeConfiguration<Log>
    {
        public void Configure(EntityTypeBuilder<Log> builder)
        {
            // Primary Key
            builder.HasKey(l => l.Id);

            // Severity & Filtering
            builder.Property(l => l.Level)
                .IsRequired()
                .HasMaxLength(15);

            // Request Tracing (for correlation)
            builder.Property(l => l.CorrelationId)
                .HasMaxLength(50);

            builder.Property(l => l.RequestId)
                .HasMaxLength(50);

            // HTTP Context
            builder.Property(l => l.HttpMethod)
                .HasMaxLength(10);

            builder.Property(l => l.Url)
                .IsRequired()
                .HasMaxLength(2000);

            // User Context
            builder.Property(l => l.UserId)
                .HasMaxLength(450);

            builder.Property(l => l.UserName)
                .HasMaxLength(256);

            // Client Context
            builder.Property(l => l.ClientIp)
                .HasMaxLength(50);

            builder.Property(l => l.UserAgent)
                .HasMaxLength(500);

            // Message Content (keep as TEXT - unlimited)
            builder.Property(l => l.Message)
                .IsRequired()
                .HasColumnType("text");

            builder.Property(l => l.Exception)
                .IsRequired()
                .HasColumnType("text");

            builder.Property(l => l.Trace)
                .IsRequired()
                .HasColumnType("text");

            // Source & Channel
            builder.Property(l => l.Logger)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(l => l.Channel)
                .HasMaxLength(100);

            // Detailed Data (keep as TEXT - unlimited)
            builder.Property(l => l.RequestHeaders)
                .HasColumnType("text");

            builder.Property(l => l.RequestBody)
                .HasColumnType("text");

            builder.Property(l => l.ResponseBody)
                .HasColumnType("text");

            builder.Property(l => l.Properties)
                .HasColumnType("text");
        }
    }
}