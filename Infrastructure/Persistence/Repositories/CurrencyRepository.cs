using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Persistence.Repositories
{
    public class CurrencyRepository : Repository<Currency>, ICurrencyRepository
    {
        private readonly IApplicationDbContext _context;

        public CurrencyRepository(IApplicationDbContext context) : base(context)
        {
            _context = context;
        }
    }
}
