using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories
{
    public class LoanApplicationRepository : Repository<LoanApplication>, ILoanApplicationRepository
    {
        private readonly IApplicationDbContext _context;

        public LoanApplicationRepository(IApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default) =>
            await _context.LoanApplications.CountAsync(cancellationToken);

        public async Task<IEnumerable<LoanApplication>> GetPaginatedListAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            return await _context.LoanApplications
                .AsNoTracking()
                .Include(a => a.Currency)
                .Include(a => a.LoanType)
                .OrderByDescending(a => a.Created)
                .ThenByDescending(a => a.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }
    }
}
