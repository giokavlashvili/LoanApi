using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class OtpVerificationConfiguration : IEntityTypeConfiguration<OtpVerification>
    {
        public void Configure(EntityTypeBuilder<OtpVerification> builder)
        {
            builder.Property(o => o.ChallengeId)
                .IsRequired();

            builder.Property(o => o.Purpose)
                .IsRequired()
                .HasMaxLength(128);

            // 256, not 32: an email address does not fit in 32, and widening later would mean
            // rebuilding both indexes below. At this width the widest key is ~776 bytes, well
            // inside SQL Server's 1700 byte nonclustered limit.
            builder.Property(o => o.Recipient)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(o => o.Channel)
                .IsRequired();

            builder.Property(o => o.UserId)
                .HasMaxLength(128);

            // Base64 of an HMAC-SHA256, so always 44 characters.
            builder.Property(o => o.CodeHash)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(o => o.RequestHash)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(o => o.ExpiresAt)
                .IsRequired();

            builder.Property(o => o.AttemptCount)
                .IsRequired();

            builder.Property(o => o.MaxAttempts)
                .IsRequired();

            builder.Property(o => o.Status)
                .IsRequired();

            builder.Property(o => o.Created)
                .IsRequired();

            builder.Property(o => o.RowVersion)
                .IsRowVersion();

            // Every lookup during verification goes through the public handle, never the PK.
            builder.HasIndex(o => o.ChallengeId)
                .IsUnique()
                .HasDatabaseName("IX_OtpVerifications_ChallengeId");

            // Covers the throttle check, which runs on every issue and is the hottest query here.
            builder.HasIndex(o => new { o.Recipient, o.Purpose, o.Created })
                .HasDatabaseName("IX_OtpVerifications_Recipient_Purpose_Created");

            // At most one live challenge per recipient, purpose and channel. The application also
            // checks this before inserting, but that read-then-write races: two concurrent issues
            // both see no predecessor and both insert. This is the only place the rule can actually
            // be enforced. 0 is OtpVerificationStatus.Pending.
            //
            // Channel is part of the key so a user whose phone and email are both on file can hold
            // one live challenge per channel for the same operation.
            builder.HasIndex(o => new { o.Recipient, o.Purpose, o.Channel })
                .IsUnique()
                .HasFilter("[Status] = 0")
                .HasDatabaseName("UX_OtpVerifications_Recipient_Purpose_Channel_Pending");
        }
    }
}
