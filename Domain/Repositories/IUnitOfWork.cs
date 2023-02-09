namespace Domain.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        ICurrencyRepository CurrencyRepository { get; }
        ILoanTypeRepository LoanTypeRepository { get; }
        ILoanApplicationRepository LoanApplicationRepository { get; }
        int Save();
        Task<int> SaveAsync(CancellationToken cancellationToken = default);
    }
}
