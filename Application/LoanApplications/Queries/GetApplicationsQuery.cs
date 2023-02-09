using Application.Common.Models;
using Application.LoanApplications.Dtos;
using AutoMapper;
using Domain.Repositories;
using MediatR;

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
        private readonly IUnitOfWork _unitOfWork;
        public GetApplicationsQueryHandler(IMapper mapper, IUnitOfWork uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task<PaginatedList<LoanApplicationDto>> Handle(GetApplicationsQuery request, CancellationToken cancellationToken)
        {
            var totalCount = await _unitOfWork.LoanApplicationRepository.GetCountAsync();

            var entities =  await _unitOfWork.LoanApplicationRepository.GetPaginatedListAsync(request.PageNumber, request.PageSize);

            var entityDtos = _mapper.Map<List<LoanApplicationDto>>(entities);

            return new PaginatedList<LoanApplicationDto>(entityDtos, totalCount, request.PageNumber, request.PageSize);
        }
    }
}
