using Application.Common.Interfaces;
using Domain.Common;
using Domain.Exceptions;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Linq.Expressions;

namespace Infrastructure.Persistence.Repositories
{
    public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
    {
        private readonly IApplicationDbContext _context;
        private readonly DbSet<TEntity> _dbSet;

        public Repository(IApplicationDbContext context)
        {
            _context = context;
            _dbSet = _context.Set<TEntity>();
        }

        // ---------------------------------------------------------------- composable queries

        public virtual IQueryable<TEntity> Query() => _dbSet;

        public virtual IQueryable<TEntity> QueryAsNoTracking() => _dbSet.AsNoTracking();

        /// <summary>
        /// Order of composition matters: <c>Include</c> and <c>Where</c> before the ordering, so the
        /// <see cref="IOrderedQueryable{T}"/> the caller's <c>orderBy</c> returns is the outermost
        /// operator and <c>Skip</c>/<c>Take</c> can build on it.
        /// </summary>
        private IQueryable<TEntity> Compose(
            Expression<Func<TEntity, bool>>? filter,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include,
            bool asNoTracking)
        {
            IQueryable<TEntity> query = asNoTracking ? _dbSet.AsNoTracking() : _dbSet;

            if (include is not null)
                query = include(query);

            if (filter is not null)
                query = query.Where(filter);

            return orderBy is not null ? orderBy(query) : query;
        }

        // ---------------------------------------------------------------- reads

        public virtual async Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            // The key array is explicit: FindAsync(id, cancellationToken) binds the
            // params object?[] overload and treats the token as a second key value.
            return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
        }

        public virtual async Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> filter,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            return await Compose(filter, orderBy: null, include, asNoTracking)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public virtual async Task<TEntity?> SingleOrDefaultAsync(
            Expression<Func<TEntity, bool>> filter,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            return await Compose(filter, orderBy: null, include, asNoTracking)
                .SingleOrDefaultAsync(cancellationToken);
        }

        public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            return await Compose(filter, orderBy, include, asNoTracking)
                .ToListAsync(cancellationToken);
        }

        public virtual async Task<(IReadOnlyList<TEntity> Items, int TotalCount)> PageAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            // Localization keys, not sentences: DomainValidationException messages are resolved
            // against WebApi/Resources/localization.json by ApiExceptionFilterAttribute, so the
            // caller's culture decides the wording and this layer never holds user-facing English.
            // Same two keys GetApplicationsQueryValidator uses, so a bad page reads identically
            // whether it was rejected at the edge or here.
            if (pageIndex < 1)
                throw new DomainValidationException("InvalidPageNumber");

            if (pageSize < 1)
                throw new DomainValidationException("InvalidPageSize");

            // Skip/Take with no ordering is nondeterministic: EF emits ORDER BY (SELECT 1), which
            // leaves SQL Server free to return rows in any order, so the same row can show up on
            // two pages while another never appears. (EF flags this as
            // RowLimitingOperationWithoutOrderByWarning.) Fall back to the key so paging is always
            // stable, even when the caller forgets to order.
            var effectiveOrderBy = orderBy ?? (q => q.OrderBy(e => e.Id));

            // The count is composed without the include shaper — the joins it adds cannot change
            // COUNT(*) and only cost work.
            var totalCount = await Compose(filter, orderBy: null, include: null, asNoTracking)
                .CountAsync(cancellationToken);

            var items = await Compose(filter, effectiveOrderBy, include, asNoTracking)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public virtual async Task<bool> AnyAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            CancellationToken cancellationToken = default)
        {
            return filter is null
                ? await _dbSet.AnyAsync(cancellationToken)
                : await _dbSet.AnyAsync(filter, cancellationToken);
        }

        public virtual async Task<int> CountAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            CancellationToken cancellationToken = default)
        {
            return filter is null
                ? await _dbSet.CountAsync(cancellationToken)
                : await _dbSet.CountAsync(filter, cancellationToken);
        }

        // ---------------------------------------------------------------- writes

        public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _dbSet.AddAsync(entity, cancellationToken);
        }

        public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _dbSet.AddRangeAsync(entities, cancellationToken);
        }

        /// <summary>
        /// Only a detached aggregate is handed to <c>DbSet.Update</c>. For one the context is
        /// already tracking, this is a deliberate no-op: the change tracker holds the original
        /// values and knows the delta, whereas <c>Update</c> marks every property modified and
        /// rewrites the whole row — including bumping the <c>RowVersion</c> token for columns that
        /// never changed.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The entity is detached and its concurrency token is a shadow property, which PostgreSQL's
        /// <c>xmin</c> is. See <see cref="GuardAgainstShadowConcurrencyToken"/>.
        /// </exception>
        public virtual void Update(TEntity entity)
        {
            var entry = _context.Entry(entity);

            if (entry.State == EntityState.Detached)
            {
                GuardAgainstShadowConcurrencyToken(entry, nameof(Update));

                _dbSet.Update(entity);
            }
        }

        public virtual void UpdateRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                Update(entity);
            }
        }

        /// <inheritdoc cref="Update"/>
        public virtual void Remove(TEntity entity)
        {
            var entry = _context.Entry(entity);

            if (entry.State == EntityState.Detached)
            {
                // Same trap as Update: EF puts the concurrency token in the DELETE's WHERE clause
                // too, so a detached remove fails its concurrency check for exactly the same reason.
                GuardAgainstShadowConcurrencyToken(entry, nameof(Remove));

                _dbSet.Attach(entity);
            }
            _dbSet.Remove(entity);
        }

        /// <summary>
        /// Rejects a detached write when the entity's concurrency token is a shadow property, which
        /// is the case for every aggregate on PostgreSQL — the token there is the system
        /// <c>xmin</c> column (see <c>Configurations/Providers/PostgresModelConfiguration.cs</c>).
        /// <para>
        /// A shadow property's value lives in the change tracker's entry, not on the entity, so
        /// detaching discards it. <c>Update</c>/<c>Attach</c> then take the token's original value to
        /// be the CLR default, EF writes <c>WHERE xmin = 0</c>, no row matches, and
        /// <c>SaveChangesAsync</c> raises <see cref="DbUpdateConcurrencyException"/> — which reads as
        /// "someone else changed this row" when in fact nothing did. On SQL Server the same call
        /// works, because <c>RowVersion</c> is a real property that travels with the detached
        /// instance.
        /// </para>
        /// <para>
        /// Throwing here rather than letting the save fail is the whole point: the exception names
        /// the real problem at the call site instead of surfacing a misleading concurrency conflict
        /// one layer away. Load the aggregate through this repository and mutate it — the tracked
        /// path works identically on both providers, and it is what every handler in this template
        /// already does.
        /// </para>
        /// </summary>
        private static void GuardAgainstShadowConcurrencyToken(EntityEntry entry, string operation)
        {
            var shadowToken = entry.Metadata.GetProperties()
                .FirstOrDefault(property => property.IsConcurrencyToken && property.IsShadowProperty());

            if (shadowToken is null)
                return;

            throw new InvalidOperationException(
                $"{typeof(TEntity).Name}.{operation} was called with a detached entity, but this "
                + $"provider's concurrency token ('{shadowToken.Name}') is a shadow property whose "
                + "value cannot survive detachment. The write would fail its concurrency check even "
                + "though nothing else changed the row. Load the entity through this repository and "
                + "mutate the tracked instance instead.");
        }

        public virtual void RemoveRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                Remove(entity);
            }
        }
    }
}
