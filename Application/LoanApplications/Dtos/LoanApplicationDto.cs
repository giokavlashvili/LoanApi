using Application.Common.Mappings;
using Application.Currencies.Dtos;
using Application.LoanTypes.Dtos;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanApplications.Dtos
{
    public class LoanApplicationDto : IMapFrom<LoanApplication>
    {
        public decimal Amount { get; private set; }
        public int PeriodPerMonth { get; private set; }
        public LoanStatus Status { get; private set; }
        public LoanTypeDto? LoanType { get; private set; }
        public CurrencyDto? Currency { get; private set; }

        public void Mapping(Profile Profile)
        {
            Profile.CreateMap<LoanApplication, LoanApplicationDto>();
        }
    }
}
