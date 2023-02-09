using Application.Common.Interfaces;
using Domain.Entities;
using Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
    {
        private readonly IMediator _mediator;
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator) : base(options)
        {
            _mediator = mediator;
        }

        public DbSet<Log> Logs { get; set; }
        public DbSet<LoanApplication> LoanApplications { get; set; }
        public DbSet<LoanType> LoanTypes { get; set; }
        public DbSet<Currency> Currencies { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            base.OnModelCreating(builder);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _mediator.DispatchDomainEvents(this);

            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            _mediator.DispatchDomainEvents(this).GetAwaiter().GetResult();

            return base.SaveChanges();
        }
    }
}
