using Application.Common.Interfaces;
using Domain.Entities;
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
    }
}
