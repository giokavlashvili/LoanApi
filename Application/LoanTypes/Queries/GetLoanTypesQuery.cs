using Application.Common.Interfaces;
using Application.LoanTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.LoanTypes.Queries
{
    public record GetLoanTypesQuery : IQuery<List<LoanTypeDto>>;

    public class GetLoanTypesQueryHandler : IRequestHandler<GetLoanTypesQuery, List<LoanTypeDto>>
    {
        private readonly IApplicationDbContext _context;

        public GetLoanTypesQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<LoanTypeDto>> Handle(GetLoanTypesQuery request, CancellationToken cancellationToken)
            => await _context.LoanTypes
                .AsNoTracking()
                .Select(t => new LoanTypeDto
                {
                    Id = t.Id,
                    Name = t.Name
                })
                .ToListAsync(cancellationToken);
    }
}
