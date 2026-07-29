using Domain.Entities;

namespace Domain.Repositories
{
    public interface ILoanApplicationRepository : IRepository<LoanApplication>
    {
        Task<int> GetCountAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<LoanApplication>> GetPaginatedListAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    }
}
