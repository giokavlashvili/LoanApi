using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.Property(r => r.SessionId)
                .IsRequired();

            builder.Property(r => r.UserId)
                .IsRequired()
                .HasMaxLength(128);

            // Base64 of an HMAC-SHA256, so always 44 characters.
            builder.Property(r => r.TokenHash)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(r => r.ExpiresAt)
                .IsRequired();

            builder.Property(r => r.SessionExpiresAt)
                .IsRequired();

            builder.Property(r => r.CreatedByIp)
                .HasMaxLength(64);

            builder.Property(r => r.UserAgent)
                .HasMaxLength(512);

            builder.Property(r => r.Status)
                .IsRequired();

            builder.Property(r => r.Created)
                .IsRequired();

            // RowVersion is configured per provider; see LoanApplicationConfiguration for the note.

            // The presented token is looked up by hash and never by PK, and the value is globally
            // unique by construction, so the unique index is both the access path and the guard
            // against a duplicate ever being stored.
            builder.HasIndex(r => r.TokenHash)
                .IsUnique()
                .HasDatabaseName("UX_RefreshTokens_TokenHash");

            // Revoking a lineage reads every row sharing the session, including the spent ones, so
            // this one is deliberately unfiltered.
            builder.HasIndex(r => r.SessionId)
                .HasDatabaseName("IX_RefreshTokens_SessionId");

            // At most one live token per lineage. The rotation path also enforces this by consuming
            // the old row in the same save, but that read-then-write races: two concurrent refreshes
            // of the same session could each insert a replacement. This is where the rule actually
            // holds. 0 is RefreshTokenStatus.Active.
            //
            // Named explicitly because it covers the same property as the index above: without a
            // distinct model name, HasIndex returns the existing builder and reconfigures it
            // instead of adding a second index.
            builder.HasIndex(r => r.SessionId, "UX_RefreshTokens_SessionId_Active")
                .IsUnique()
                .HasFilter("[Status] = 0")
                .HasDatabaseName("UX_RefreshTokens_SessionId_Active");

            // Supports revoking or auditing everything belonging to one user.
            builder.HasIndex(r => new { r.UserId, r.Status })
                .HasDatabaseName("IX_RefreshTokens_UserId_Status");
        }
    }
}
