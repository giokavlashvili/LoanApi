using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories
{
    public class OtpVerificationRepository : Repository<OtpVerification>, IOtpVerificationRepository
    {
        private readonly IApplicationDbContext _context;

        public OtpVerificationRepository(IApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<OtpVerification?> GetByChallengeIdAsync(Guid challengeId, CancellationToken cancellationToken = default)
        {
            return await _context.OtpVerifications
                .FirstOrDefaultAsync(o => o.ChallengeId == challengeId, cancellationToken);
        }

        public async Task<int> CountRecentAsync(string recipient, string purpose, DateTime since, CancellationToken cancellationToken = default)
        {
            return await _context.OtpVerifications
                .AsNoTracking()
                .CountAsync(o => o.Recipient == recipient && o.Purpose == purpose && o.Created >= since, cancellationToken);
        }

        public async Task<OtpVerification?> GetLatestAsync(string recipient, string purpose, CancellationToken cancellationToken = default)
        {
            return await _context.OtpVerifications
                .Where(o => o.Recipient == recipient && o.Purpose == purpose)
                .OrderByDescending(o => o.Created)
                .ThenByDescending(o => o.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
