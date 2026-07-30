using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Persistence.Repositories
{
    public class LoanApplicationRepository : Repository<LoanApplication>, ILoanApplicationRepository
    {
        public LoanApplicationRepository(IApplicationDbContext context) : base(context)
        {
        }
    }
}
