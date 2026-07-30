using Application.Common.Interfaces;
using Domain.Entities;
using Infrastructure.Common.Extensions;
using Infrastructure.Identity;
using Infrastructure.Persistence.Configurations.Providers;
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
        public DbSet<OtpVerification> OtpVerifications { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Where the two relational providers disagree, the difference is applied here rather than
            // in the shared configurations above. Both branches produce the same model those
            // configurations used to, so ApplicationDbContextModelSnapshot and the applied SQL
            // Server migrations are unaffected.
            //
            // The in-memory provider gets neither: it enforces no concurrency tokens and ignores
            // index filters, so there is nothing for either pass to do.
            if (Database.IsNpgsql())
            {
                PostgresModelConfiguration.Apply(builder);
            }
            else if (Database.IsSqlServer())
            {
                SqlServerModelConfiguration.Apply(builder);
            }

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
