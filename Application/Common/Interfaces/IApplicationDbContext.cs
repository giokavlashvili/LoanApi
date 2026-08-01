using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Application.Common.Interfaces
{
    public interface IApplicationDbContext : IDisposable
    {
        public DbSet<Log> Logs { get; set; }
        public DbSet<LoanApplication> LoanApplications { get; set; }
        public DbSet<LoanType> LoanTypes { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<OtpVerification> OtpVerifications { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<TEntity> Set<TEntity>() where TEntity : class;

        public EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken);

        public int SaveChanges();

        public DatabaseFacade Database { get; }

        public IDbContextTransaction? GetCurrentTransaction();

        public Task<IDbContextTransaction?> BeginTransactionAsync();

        public Task CommitTransactionAsync(IDbContextTransaction transaction, CancellationToken cancellationToken = default);

        public void RollbackTransaction();

        public bool HasActiveTransaction { get; }
    }
}