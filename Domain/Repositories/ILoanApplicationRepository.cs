using Domain.Entities;

namespace Domain.Repositories
{
    public interface ILoanApplicationRepository : IRepository<LoanApplication>
    {
        Task<int> GetCountAsync();
        Task<IEnumerable<LoanApplication>> GetPaginatedListAsync(int pageIndex, int pageSize);
    }
}
