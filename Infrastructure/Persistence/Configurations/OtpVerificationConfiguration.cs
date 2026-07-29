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

            builder.Property(o => o.Recipient)
                .IsRequired()
                .HasMaxLength(32);

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

            // Every lookup during verification goes through the public handle, never the PK.
            builder.HasIndex(o => o.ChallengeId)
                .IsUnique()
                .HasDatabaseName("IX_OtpVerifications_ChallengeId");

            // Covers the throttle check, which runs on every issue and is the hottest query here.
            builder.HasIndex(o => new { o.Recipient, o.Purpose, o.Created })
                .HasDatabaseName("IX_OtpVerifications_Recipient_Purpose_Created");
        }
    }
}
