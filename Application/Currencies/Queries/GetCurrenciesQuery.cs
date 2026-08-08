using Application.Common.Interfaces;
using Application.Currencies.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Currencies.Queries
{
    public record GetCurrenciesQuery : IQuery<List<CurrencyDto>>;

    public class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, List<CurrencyDto>>
    {
        private readonly IApplicationDbContext _context;

        public GetCurrenciesQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        // Read side: no repository, no entity materialisation. The Select is the SELECT list, so
        // only the columns the DTO declares leave the database.
        public async Task<List<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
            => await _context.Currencies
                .AsNoTracking()
                .Select(c => new CurrencyDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync(cancellationToken);
    }
}
