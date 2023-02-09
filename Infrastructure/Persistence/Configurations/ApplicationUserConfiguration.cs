using Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            builder.Property(u => u.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(u => u.LastName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(u => u.PersonalNumber)
                .HasMaxLength(11)
                .IsRequired();
        }
    }
}
