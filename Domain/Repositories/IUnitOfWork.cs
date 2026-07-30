namespace Domain.Repositories
{
    /// <summary>
    /// One transactional boundary per request. Repositories are injected directly into the
    /// handlers that use them — this exists only to commit.
    /// </summary>
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
