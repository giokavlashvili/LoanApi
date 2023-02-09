using Application.Common.Interfaces;
using Domain.Repositories;

namespace Infrastructure.Persistence.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private bool disposed = false;
        private readonly IApplicationDbContext _context;

        public UnitOfWork(
            IApplicationDbContext context, 
            ICurrencyRepository? currencyRepository = null,
            ILoanTypeRepository? loanTypeRepository = null,
            ILoanApplicationRepository? loanApplicationRepository = null)
        {
            _context = context;
            CurrencyRepository = currencyRepository ?? new CurrencyRepository(_context);
            LoanTypeRepository = loanTypeRepository ?? new LoanTypeRepository(_context);
            LoanApplicationRepository = loanApplicationRepository ?? new LoanApplicationRepository(_context);
        }

        public ICurrencyRepository CurrencyRepository { get; private set; }
        public ILoanTypeRepository LoanTypeRepository { get; private set; }
        public ILoanApplicationRepository LoanApplicationRepository { get; private set; }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    _context.Dispose();
                }
            }
            this.disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public int Save()
        {
            return _context.SaveChanges();
        }

        public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
