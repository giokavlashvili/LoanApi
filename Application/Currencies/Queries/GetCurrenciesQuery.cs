using Application.Common.Interfaces;
using Application.Currencies.Dtos;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Currencies.Queries
{
    public record GetCurrenciesQuery : IRequest<List<CurrencyDto>>;

    public class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, List<CurrencyDto>>
    {
        private readonly IMapper _mapper;
        private readonly IApplicationDbContext _context;

        public GetCurrenciesQueryHandler(IMapper mapper, IApplicationDbContext context)
        {
            _mapper = mapper;
            _context = context;
        }

        // Read side: no repository, no entity materialisation. ProjectTo turns the DTO's shape
        // into the SELECT list, so only the columns the DTO declares leave the database.
        public async Task<List<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
            => await _context.Currencies
                .AsNoTracking()
                .ProjectTo<CurrencyDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);
    }
}
