using Domain.Entities;

namespace Domain.Repositories
{
    /// <summary>
    /// The write side of the loan application aggregate. Listing and paging live in the query
    /// handlers, which project from the context rather than materialising aggregates.
    /// </summary>
    public interface ILoanApplicationRepository : IRepository<LoanApplication>
    {
    }
}
