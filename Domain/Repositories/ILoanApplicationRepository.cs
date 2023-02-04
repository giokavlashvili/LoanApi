using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Repositories
{
    public interface ILoanApplicationRepository : IRepository<LoanApplication>
    {
        Task<int> GetCountAsync();
        Task<IEnumerable<LoanApplication>> GetPaginatedListAsync(int pageIndex, int pageSize);
    }
}
