using Application.Common.Interfaces;
using Domain.Common;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Persistence.Repositories
{
    public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
    {
        private readonly IApplicationDbContext _context;
        private readonly DbSet<TEntity> dbSet;
        public Repository(IApplicationDbContext context)
        {
            _context = context;
            this.dbSet = _context.Set<TEntity>();
        }

        public virtual void Add(TEntity entity)
        {
            dbSet.Add(entity);
        }

        public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await dbSet.AddAsync(entity, cancellationToken);
        }

        public virtual void AddRange(IEnumerable<TEntity> entities)
        {
            dbSet.AddRange(entities);
        }

        public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await dbSet.AddRangeAsync(entities, cancellationToken);
        }

        public virtual TEntity? GetById(int id)
        {
            return dbSet.Find(id);
        }

        public virtual async Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await dbSet.FindAsync(id, cancellationToken);
        }

        public virtual IEnumerable<TEntity> Get(
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            string includeProperties = "")
        {
            IQueryable<TEntity> query = dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }

            foreach (var includeProperty in includeProperties.Split
                (new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty);
            }

            if (orderBy != null)
            {
                return orderBy(query).ToList();
            }
            else
            {
                return query.ToList();
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetAsync(
            Expression<Func<TEntity, bool>>? filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            string includeProperties = "",
            CancellationToken cancellationToken = default)
        {
            IQueryable<TEntity> query = dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }

            foreach (var includeProperty in includeProperties.Split
                (new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty);
            }

            if (orderBy != null)
            {
                return await orderBy(query).ToListAsync(cancellationToken);
            }
            else
            {
                return await query.ToListAsync(cancellationToken);
            }
        }

        public virtual IEnumerable<TEntity> GetAll()
        {
            return dbSet.ToList();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            return await dbSet.ToListAsync();
        }

        public virtual void Remove(TEntity entity)
        {
            if (_context.Entry(entity).State == EntityState.Detached)
            {
                dbSet.Attach(entity);
            }
            dbSet.Remove(entity);
        }

        public virtual void RemoveRange(IEnumerable<TEntity> entities)
        {
            dbSet.RemoveRange(entities);
        }

        public virtual void Update(TEntity entityToUpdate)
        {
            dbSet.Update(entityToUpdate);
        }

        public virtual void UpdateRange(IEnumerable<TEntity> entities)
        {
            dbSet.UpdateRange(entities);
        }
    }
}
