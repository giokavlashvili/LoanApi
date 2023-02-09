using Application.Currencies.Dtos;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Currencies.Queries
{
    public record GetCurrenciesQuery : IRequest<List<CurrencyDto>>;

    public class GetCurrenciesQueryHandler : IRequestHandler<GetCurrenciesQuery, List<CurrencyDto>>
    {
        private readonly IMapper _mapper;
        private readonly IUnitOfWork _unitOfWork;
        public GetCurrenciesQueryHandler(IMapper mapper, IUnitOfWork uow)
        {
            _mapper = mapper;
            _unitOfWork = uow;
        }

        public async Task<List<CurrencyDto>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
        {
            var resultList = await _unitOfWork.CurrencyRepository.GetAllAsync();
            return _mapper.Map<List<CurrencyDto>>(resultList);
        }
    }
}
