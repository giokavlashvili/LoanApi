using Domain.Entities;
using Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence.Repositories
{
    public class LoanTypeRepository : Repository<LoanType>, ILoanTypeRepository
    {
        private readonly ApplicationDbContext _context;

        public LoanTypeRepository(ApplicationDbContext context):base(context)
        {
            _context = context;
        }
    }
}
