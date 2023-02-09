using Application.Currencies.Dtos;
using Application.Currencies.Queries;
using Application.LoanTypes.Dtos;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanTypes.Queries
{
    public record GetLoanTypesQuery : IRequest<List<LoanTypeDto>>;

    public class GetLoanTypesQueryHandler : IRequestHandler<GetLoanTypesQuery, List<LoanTypeDto>>
    {
        private readonly IMapper _mapper;
        private readonly IUnitOfWork _unitOfWork;
        public GetLoanTypesQueryHandler(IMapper mapper, IUnitOfWork uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task<List<LoanTypeDto>> Handle(GetLoanTypesQuery request, CancellationToken cancellationToken)
        {
            var resultList = await _unitOfWork.LoanTypeRepository.GetAllAsync();
            return _mapper.Map<List<LoanTypeDto>>(resultList);
        }
    }
}
