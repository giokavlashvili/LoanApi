using Application.Common.Interfaces;
using Application.Common.Models;
using Application.LoanApplications.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.LoanApplications.Queries
{
    public record GetApplicationsQuery : IQuery<PaginatedList<LoanApplicationDto>>
    {
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 20;
    }

    public class GetApplicationsQueryHandler : IRequestHandler<GetApplicationsQuery, PaginatedList<LoanApplicationDto>>
    {
        private readonly IApplicationDbContext _context;

        public GetApplicationsQueryHandler(IApplicationDbContext context)
        {
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

            // The Select replaces the Include(a => a.Currency).Include(a => a.LoanType) pair: the
            // projection joins and selects exactly the columns the DTO needs. Both navigations are
            // optional on the entity, so each name is null-guarded down to "" rather than assuming
            // the join found a row — EF translates the conditional to a CASE over the LEFT JOIN.
            var items = await query
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(a => new LoanApplicationDto
                {
                    Id = a.Id,
                    Amount = a.Amount,
                    PeriodPerMonth = a.PeriodPerMonth,
                    Status = a.Status,
                    LoanType = a.LoanType != null && a.LoanType.Name != null ? a.LoanType.Name : "",
                    Currency = a.Currency != null && a.Currency.Name != null ? a.Currency.Name : "",
                    Created = a.Created
                })
                .ToListAsync(cancellationToken);

            return new PaginatedList<LoanApplicationDto>(items, totalCount, request.PageNumber, request.PageSize);
        }
    }
}
