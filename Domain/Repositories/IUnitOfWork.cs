namespace Domain.Repositories
{
    public interface IUnitOfWork
    {
        ICurrencyRepository CurrencyRepository { get; }
        ILoanTypeRepository LoanTypeRepository { get; }
        ILoanApplicationRepository LoanApplicationRepository { get; }
        IOtpVerificationRepository OtpVerificationRepository { get; }
        int Save();
        Task<int> SaveAsync(CancellationToken cancellationToken = default);
    }
}
