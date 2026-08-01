using Domain.Entities;

namespace Domain.Repositories
{
    public interface IPendingOperationRepository : IRepository<PendingOperation>
    {
        /// <summary>
        /// Tracked load by the public handle. Every lookup goes through <c>OperationId</c>, never
        /// the sequential primary key, which never leaves the server.
        /// </summary>
        Task<PendingOperation?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    }
}
