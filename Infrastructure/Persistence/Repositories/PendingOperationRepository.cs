using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories
{
    public class PendingOperationRepository : Repository<PendingOperation>, IPendingOperationRepository
    {
        private readonly IApplicationDbContext _context;

        public PendingOperationRepository(IApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<PendingOperation?> GetByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default)
        {
            return await _context.PendingOperations
                .FirstOrDefaultAsync(o => o.OperationId == operationId, cancellationToken);
        }

        public async Task<IReadOnlyList<PendingOperation>> ListPendingForUserAsync(string userId, DateTime now, CancellationToken cancellationToken = default)
        {
            return await PendingForUser(userId, now)
                .AsNoTracking()
                .OrderByDescending(o => o.Created)
                .ThenByDescending(o => o.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> CountPendingForUserAsync(string userId, DateTime now, CancellationToken cancellationToken = default)
        {
            return await PendingForUser(userId, now).CountAsync(cancellationToken);
        }

        /// <remarks>
        /// Filters out rows that have passed <c>ExpiresAt</c> but are still marked Pending. Expiry
        /// is evaluated on read — there is no sweeper — so an expired row would otherwise count
        /// against the cap and show up in the listing forever.
        /// </remarks>
        private IQueryable<PendingOperation> PendingForUser(string userId, DateTime now) =>
            _context.PendingOperations
                .Where(o => o.InitiatedBy == userId
                            && o.Status == PendingOperationStatus.Pending
                            && o.ExpiresAt > now);
    }
}
