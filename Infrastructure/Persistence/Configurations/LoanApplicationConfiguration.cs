using Domain.Entities;
using Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        }
    }
}
