using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories
{
    public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
    {
        private readonly IApplicationDbContext _context;

        public RefreshTokenRepository(IApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return await _context.RefreshTokens
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);
        }

        public async Task<IReadOnlyList<RefreshToken>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            // Tracked: the caller loads these to revoke them, and a no-tracking read would discard
            // every mutation silently.
            return await _context.RefreshTokens
                .Where(r => r.SessionId == sessionId)
                .ToListAsync(cancellationToken);
        }
    }
}
