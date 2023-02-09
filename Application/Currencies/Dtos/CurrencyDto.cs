using Application.Common.Mappings;
using AutoMapper;
using Domain.Entities;

namespace Application.Currencies.Dtos
{
    public class CurrencyDto : IMapFrom<Currency>
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        public void Mapping(Profile Profile)
        {
            Profile.CreateMap<Currency, CurrencyDto>();
        }
    }
}
