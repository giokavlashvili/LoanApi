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

            builder.Property(o => o.ChallengeId)
                .IsRequired();

            builder.Property(o => o.OperationType)
                .IsRequired()
                .HasMaxLength(128);

            // nvarchar(max): the payload is whatever the operation's command happens to hold.
            // Nulled out once the operation reaches a terminal state.
            builder.Property(o => o.Payload);

            builder.Property(o => o.ResultPayload);

            builder.Property(o => o.ErrorKey)
                .HasMaxLength(128);

            builder.Property(o => o.UserId)
                .HasMaxLength(128);

            builder.Property(o => o.Status)
                .IsRequired();

            builder.Property(o => o.Created)
                .IsRequired();

            builder.Property(o => o.RowVersion)
                .IsRowVersion();

            // Every lookup goes through the public handle, never the PK.
            builder.HasIndex(o => o.OperationId)
                .IsUnique()
                .HasDatabaseName("IX_PendingOperations_OperationId");

            // Confirm resolves the operation from its own id, but a resend arrives holding only
            // the challenge, so it has to find the operation that way.
            builder.HasIndex(o => o.ChallengeId)
                .HasDatabaseName("IX_PendingOperations_ChallengeId");

            // Nothing in the application deletes these rows — see the retention script in
            // docs/plans/06. This is the index that script needs to not table-scan.
            builder.HasIndex(o => new { o.Status, o.Created })
                .HasDatabaseName("IX_PendingOperations_Status_Created");
        }
    }
}
