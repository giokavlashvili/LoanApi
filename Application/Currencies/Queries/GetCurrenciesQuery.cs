using Application.Currencies.Dtos;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Application.Currencies.Queries
{
    public record GetCurrenciesQuery : IRequest<List<CurrencyDto>>;

    public class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, List<CurrencyDto>>
    {
        private readonly IMapper _mapper;
        private readonly IUnitOfWork _uow;
        public GetCurrenciesQueryHandler(IMapper mapper, IUnitOfWork uow)
        {
            _mapper = mapper;
            _uow = uow;
        }

        public async Task<List<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
        {
            var resultList = await _uow.CurrencyRepository.GetAllAsync();
            return _mapper.Map<List<CurrencyDto>>(resultList);
        }
    }
}
