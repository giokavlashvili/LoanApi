using Application.Common.Mappings;
using Application.Currencies.Dtos;
using AutoMapper;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanTypes.Dtos
{
    public class LoanTypeDto : IMapFrom<LoanType>
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        public void Mapping(Profile Profile)
        {
            Profile.CreateMap<LoanType, LoanTypeDto>();
        }
    }
}
