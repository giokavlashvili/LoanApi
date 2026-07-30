using Domain.Entities;

namespace Domain.Repositories
{
    public interface IRefreshTokenRepository : IRepository<RefreshToken>
    {
        /// <summary>
        /// The only lookup path for a presented token. The hash is deterministic, so equality in
        /// the WHERE clause does the comparison and no candidate is ever compared in memory.
        /// </summary>
        Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every token in one rotation lineage, tracked. Backs both reuse-detection revocation and
        /// logout, which have to close the spent links as well as the active one.
        /// </summary>
        Task<IReadOnlyList<RefreshToken>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    }
}
