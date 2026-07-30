using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
    {
        public void Configure(EntityTypeBuilder<LoanApplication> builder)
        {
            builder.Property(l => l.LoanTypeId)
                .IsRequired();

            builder.Property(l => l.CurrencyId)
                .IsRequired();

            builder.Property(l => l.Amount)
                .IsRequired();

            builder.Property(l => l.PeriodPerMonth)
                .IsRequired();

            builder.Property(l => l.Status)
                .IsRequired();

            builder.Property(l => l.Created)
                .IsRequired();

            builder.Property(l => l.CreatedBy)
                .IsRequired();

            // RowVersion is configured per provider: SQL Server maps it as a rowversion column,
            // PostgreSQL leaves it unmapped and uses xmin instead. See
            // Configurations/Providers/{SqlServer,Postgres}ModelConfiguration.cs.

            // These two were [ForeignKey] annotations on the entity. Declared here, the domain
            // carries no persistence mapping and the shape is unchanged: both FK properties are
            // non-nullable, so EF still infers a required relationship with cascade delete.
            builder.HasOne(l => l.LoanType)
                .WithMany()
                .HasForeignKey(l => l.LoanTypeId);

            builder.HasOne(l => l.Currency)
                .WithMany()
                .HasForeignKey(l => l.CurrencyId);
        }
    }
}
