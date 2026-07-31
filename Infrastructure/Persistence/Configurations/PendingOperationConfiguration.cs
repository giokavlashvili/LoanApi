using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class PendingOperationConfiguration : IEntityTypeConfiguration<PendingOperation>
    {
        public void Configure(EntityTypeBuilder<PendingOperation> builder)
        {
            builder.Property(o => o.OperationId)
                .IsRequired();

            builder.Property(o => o.OperationType)
                .IsRequired()
                .HasMaxLength(128);

            // Unbounded: it holds a serialized command, and capping it would fail at save time on
            // whichever command first outgrew the cap.
            builder.Property(o => o.Payload)
                .IsRequired();

            builder.Property(o => o.InitiatedBy)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(o => o.ConfirmedBy)
                .HasMaxLength(128);

            builder.Property(o => o.ExpiresAt)
                .IsRequired();

            builder.Property(o => o.Status)
                .IsRequired();

            builder.Property(o => o.Created)
                .IsRequired();

            builder.Property(o => o.RowVersion)
                .IsRowVersion();

            // Every confirm resolves through the public handle, never the PK.
            builder.HasIndex(o => o.OperationId)
                .IsUnique()
                .HasDatabaseName("IX_PendingOperations_OperationId");

            // Backs the per-user cap and the pending listing.
            builder.HasIndex(o => new { o.InitiatedBy, o.Status })
                .HasDatabaseName("IX_PendingOperations_InitiatedBy_Status");

            // Deliberately no filtered unique index of the kind OtpVerification carries. There the
            // rule is "at most one live challenge"; here many concurrent pending operations per
            // user is the intended state, not a race to suppress.
        }
    }
}
