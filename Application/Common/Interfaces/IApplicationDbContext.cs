using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        public DbSet<PendingOperation> PendingOperations { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<TEntity> Set<TEntity>() where TEntity : class;

        public EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken);

        public int SaveChanges();

        /// <summary>
        /// Throws if one is already open — callers check <see cref="HasActiveTransaction"/> first,
        /// and a silent null return would only surface later as a NullReferenceException.
        /// </summary>
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>Saves anything still tracked, commits, and clears the current transaction.</summary>
        public Task CommitTransactionAsync(IDbContextTransaction transaction, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rolls back and clears the current transaction. Disposing the transaction alone rolls the
        /// database back but leaves <see cref="HasActiveTransaction"/> reporting true against a dead
        /// object, so anything dispatched afterwards in the same scope would run untransacted.
        /// </summary>
        public void RollbackTransaction();

        public bool HasActiveTransaction { get; }
    }
}