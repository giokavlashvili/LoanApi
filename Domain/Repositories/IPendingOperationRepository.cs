using Domain.Entities;

namespace Domain.Repositories
{
    public interface IPendingOperationRepository : IRepository<PendingOperation>
    {
        /// <summary>
        /// Tracked load by the public handle. Every confirm goes through this, never the PK.
        /// </summary>
        Task<PendingOperation?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Operations one user has parked and not yet resolved. Backs both the "what am I waiting
        /// on" listing and the per-user cap, which is what stops a caller filling the table with
        /// operations they never intend to confirm.
        /// </summary>
        Task<IReadOnlyList<PendingOperation>> ListPendingForUserAsync(string userId, DateTime now, CancellationToken cancellationToken = default);

        /// <inheritdoc cref="ListPendingForUserAsync"/>
        Task<int> CountPendingForUserAsync(string userId, DateTime now, CancellationToken cancellationToken = default);
    }
}
