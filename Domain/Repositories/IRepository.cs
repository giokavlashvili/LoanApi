using Domain.Common;
using System.Linq.Expressions;

namespace Domain.Repositories
{
    /// <summary>
    /// Generic persistence surface for any <see cref="BaseEntity"/>.
    /// <para>
    /// The constraint is deliberately just <see cref="BaseEntity"/>, not
    /// <see cref="IAggregateRoot"/>: this is a reusable boilerplate repository, and a future entity
    /// that is not an aggregate root still needs somewhere to live. <see cref="IAggregateRoot"/>
    /// remains as documentation of which entities own a consistency boundary.
    /// </para>
    /// <para>
    /// Every type here is BCL (<see cref="IQueryable{T}"/>, <see cref="Expression"/>,
    /// <see cref="Task"/>) — no EF type appears in this interface, so <c>Domain</c> stays free of a
    /// persistence dependency even though the shaper delegates are composed with EF's
    /// <c>Include</c>/<c>ThenInclude</c> at the call site.
    /// </para>
    /// <para>
    /// Entity-specific, intention-revealing lookups still belong on that entity's own interface
    /// (see <see cref="IOtpVerificationRepository"/>). Reach for the generic members when a named
    /// one would add nothing.
    /// </para>
    /// </summary>
    public interface IRepository<TEntity> where TEntity : BaseEntity
    {
        // ---------------------------------------------------------------- composable queries

        /// <summary>
        /// Tracked queryable for callers that need to compose something these members do not
        /// cover — grouping, joins, a projection to a DTO. Deferred: nothing executes until the
        /// caller enumerates it, so pass a <see cref="CancellationToken"/> to whatever terminal
        /// operator is used.
        /// </summary>
        IQueryable<TEntity> Query();

        /// <summary>
        /// As <see cref="Query"/>, but the results are not tracked. Use for reads; mutations made
        /// to entities returned from here will not be persisted.
        /// </summary>
        IQueryable<TEntity> QueryAsNoTracking();

        // ---------------------------------------------------------------- reads

        /// <summary>Tracked load by primary key, or null. The normal entry point for a mutation.</summary>
        Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> filter,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);

        /// <summary>Throws if more than one row matches — use when the filter should be unique.</summary>
        Task<TEntity?> SingleOrDefaultAsync(
            Expression<Func<TEntity, bool>> filter,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// <paramref name="include"/> is a shaper, not a string: pass
        /// <c>q => q.Include(a =&gt; a.Currency)</c>. A typo is a compile error, renames follow it,
        /// and <c>ThenInclude</c> chains work.
        /// </summary>
        Task<IReadOnlyList<TEntity>> ListAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// One page plus the unpaged total. Both derive from the same query definition, so the
        /// count and the page cannot disagree the way two separate calls could.
        /// </summary>
        Task<(IReadOnlyList<TEntity> Items, int TotalCount)> PageAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);

        Task<bool> AnyAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            CancellationToken cancellationToken = default);

        Task<int> CountAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            CancellationToken cancellationToken = default);

        // ---------------------------------------------------------------- writes

        Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// For a **detached** entity — one that came from outside this context, so there is no
        /// tracked original to diff against.
        /// <para>
        /// An entity already tracked by the context does not need this: mutating it is enough,
        /// and the implementation deliberately does nothing in that case. Forcing a full-row
        /// update on a tracked entity marks every column modified, rewrites values that never
        /// changed, and needlessly churns the <c>RowVersion</c> concurrency token.
        /// </para>
        /// </summary>
        void Update(TEntity entity);

        void UpdateRange(IEnumerable<TEntity> entities);

        void Remove(TEntity entity);

        void RemoveRange(IEnumerable<TEntity> entities);
    }
}
