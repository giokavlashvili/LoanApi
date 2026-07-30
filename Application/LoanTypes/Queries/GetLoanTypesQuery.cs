using Application.Common.Interfaces;
using Application.LoanTypes.Dtos;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.LoanTypes.Queries
{
    public record GetLoanTypesQuery : IRequest<List<LoanTypeDto>>;

    public class GetLoanTypesQueryHandler : IRequestHandler<GetLoanTypesQuery, List<LoanTypeDto>>
    {
        private readonly IMapper _mapper;
        private readonly IApplicationDbContext _context;

        public GetLoanTypesQueryHandler(IMapper mapper, IApplicationDbContext context)
        {
            _mapper = mapper;
            _context = context;
        }

        public async Task<List<LoanTypeDto>> Handle(GetLoanTypesQuery request, CancellationToken cancellationToken)
            => await _context.LoanTypes
                .AsNoTracking()
                .ProjectTo<LoanTypeDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);
    }
}
