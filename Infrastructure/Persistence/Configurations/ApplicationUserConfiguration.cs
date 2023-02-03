﻿using Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Configurations
{
    public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            builder.Property(t => t.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.LastName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.PersonalNumber)
                .HasMaxLength(11)
                .IsRequired();
        }
    }
}
