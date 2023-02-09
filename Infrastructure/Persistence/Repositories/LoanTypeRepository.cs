using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Persistence.Repositories
{
    public class LoanTypeRepository : Repository<LoanType>, ILoanTypeRepository
    {
        private readonly IApplicationDbContext _context;

        public LoanTypeRepository(IApplicationDbContext context):base(context)
        {
            _context = context;
        }
    }
}
