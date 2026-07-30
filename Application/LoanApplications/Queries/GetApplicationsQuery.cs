using Application.Common.Interfaces;
using Application.Common.Models;
using Application.LoanApplications.Dtos;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.LoanApplications.Queries
{
    public record GetApplicationsQuery : IRequest<PaginatedList<LoanApplicationDto>>
    {
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 20;
    }

    public class GetApplicationsQueryHandler : IRequestHandler<GetApplicationsQuery, PaginatedList<LoanApplicationDto>>
    {
        private readonly IMapper _mapper;
        private readonly IApplicationDbContext _context;

        public GetApplicationsQueryHandler(IMapper mapper, IApplicationDbContext context)
        {
            _mapper = mapper;
            _context = context;
        }

        public async Task<PaginatedList<LoanApplicationDto>> Handle(GetApplicationsQuery request, CancellationToken cancellationToken)
        {
            // The count and the page derive from one query definition. Two repository methods
            // could drift apart — different filters, different ordering — and nothing would say so.
            var query = _context.LoanApplications
                .AsNoTracking()
                .OrderByDescending(a => a.Created)
                .ThenByDescending(a => a.Id);

            var totalCount = await query.CountAsync(cancellationToken);

            // ProjectTo replaces the Include(a => a.Currency).Include(a => a.LoanType) pair: the
            // projection joins and selects exactly the columns the DTO needs.
            var items = await query
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ProjectTo<LoanApplicationDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            return new PaginatedList<LoanApplicationDto>(items, totalCount, request.PageNumber, request.PageSize);
        }
    }
}
