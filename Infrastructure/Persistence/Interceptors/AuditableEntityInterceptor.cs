using Application.Common.Interfaces;
using Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Stamps <see cref="BaseAuditableEntity"/> audit columns at the save boundary, so no factory,
    /// mutator or handler has to thread a user id and a timestamp through its signature. Previously
    /// every call site had to remember both, and every one of them needed
    /// <c>#pragma warning disable CS8604</c> because <c>ICurrentUserService.UserId</c> is nullable.
    /// </summary>
    /// <remarks>
    /// Registered on <b>both</b> provider branches in <c>AddInfrastructureServices</c>. Attached to
    /// only the SQL Server branch, auditing would silently stop working whenever
    /// <c>UseInMemoryDatabase</c> is true — including under test, which is where it would be
    /// noticed last.
    /// <para>
    /// Note the ordering against domain events: <c>ApplicationDbContext.SaveChangesAsync</c>
    /// dispatches them <em>before</em> calling into the base implementation, and this interceptor
    /// runs inside it. A domain event handler therefore observes audit fields that have not been
    /// stamped yet. No handler reads them today; one that needs <c>Created</c> or
    /// <c>LastModifiedBy</c> must take it from the command rather than the entity.
    /// </para>
    /// </remarks>
    public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IDateTime _dateTime;

        public AuditableEntityInterceptor(ICurrentUserService currentUser, IDateTime dateTime)
        {
            _currentUser = currentUser;
            _dateTime = dateTime;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Stamp(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // Both paths, deliberately. Stamping only the async one leaves a synchronous
            // SaveChanges writing rows with no audit trail at all.
            Stamp(eventData.Context);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Stamp(DbContext? context)
        {
            if (context is null)
                return;

            var userId = _currentUser.UserId;
            var now = _dateTime.UtcNow;

            foreach (var entry in context.ChangeTracker.Entries<BaseAuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Filled in only where the entity did not set them itself. OtpVerification
                    // takes its own `created` and derives CreatedBy from the challenge's user, and
                    // the OTP resend cooldown reads that value back — overwriting it here would
                    // quietly replace a value the aggregate chose on purpose.
                    if (entry.Property(e => e.Created).CurrentValue == default)
                        entry.Property(e => e.Created).CurrentValue = now;

                    if (string.IsNullOrWhiteSpace(entry.Property(e => e.CreatedBy).CurrentValue))
                        entry.Property(e => e.CreatedBy).CurrentValue = userId;
                }
                else if (entry.State == EntityState.Modified || HasChangedOwnedEntities(entry))
                {
                    // Created is never touched here — an update must not rewrite who created the
                    // row or when.
                    entry.Property(e => e.LastModified).CurrentValue = now;
                    entry.Property(e => e.LastModifiedBy).CurrentValue = userId;
                }
            }
        }

        /// <summary>
        /// An aggregate whose own columns are untouched but one of whose owned entities changed is
        /// still a modification of the aggregate — EF reports the parent as <c>Unchanged</c> in that
        /// case, so without this the change would go unaudited. Nothing in this model is owned yet;
        /// the check is here so adding one does not silently create that gap.
        /// </summary>
        private static bool HasChangedOwnedEntities(EntityEntry entry) =>
            entry.References.Any(reference =>
                reference.TargetEntry is not null
                && reference.TargetEntry.Metadata.IsOwned()
                && (reference.TargetEntry.State == EntityState.Added
                    || reference.TargetEntry.State == EntityState.Modified));
    }
}
