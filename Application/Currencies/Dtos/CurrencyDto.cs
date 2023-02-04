using Application.Common.Mappings;
using AutoMapper;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
