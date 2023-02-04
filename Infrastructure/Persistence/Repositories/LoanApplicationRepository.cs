using Domain.Entities;
using Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Repositories
{
    public class LoanApplicationRepository : Repository<LoanApplication>, ILoanApplicationRepository
    {
        private readonly ApplicationDbContext _context;

        public LoanApplicationRepository(ApplicationDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<int> GetCountAsync() => await _context.LoanApplications.CountAsync();

        public async Task<IEnumerable<LoanApplication>> GetPaginatedListAsync(int pageIndex, int pageSize)
        {
            return await _context.LoanApplications
                .Include(a => a.Currency)
                .Include(a => a.LoanType)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}
