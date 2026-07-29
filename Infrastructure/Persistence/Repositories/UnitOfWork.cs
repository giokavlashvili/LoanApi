using Application.Common.Interfaces;
using Domain.Repositories;

namespace Infrastructure.Persistence.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IApplicationDbContext _context;

        public UnitOfWork(
            IApplicationDbContext context,
            ICurrencyRepository currencyRepository,
            ILoanTypeRepository loanTypeRepository,
            ILoanApplicationRepository loanApplicationRepository,
            IOtpVerificationRepository otpVerificationRepository)
        {
            _context = context;
            CurrencyRepository = currencyRepository;
            LoanTypeRepository = loanTypeRepository;
            LoanApplicationRepository = loanApplicationRepository;
            OtpVerificationRepository = otpVerificationRepository;
        }

        public ICurrencyRepository CurrencyRepository { get; private set; }
        public ILoanTypeRepository LoanTypeRepository { get; private set; }
        public ILoanApplicationRepository LoanApplicationRepository { get; private set; }
        public IOtpVerificationRepository OtpVerificationRepository { get; private set; }

        public virtual int Save()
        {
            return _context.SaveChanges();
        }

        public virtual async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
