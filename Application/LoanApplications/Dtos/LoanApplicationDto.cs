using Application.Common.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;

namespace Application.LoanApplications.Dtos
{
    public class LoanApplicationDto : IMapFrom<LoanApplication>
    {
        public int Id { get; set; }
        public decimal Amount { get; private set; }
        public int PeriodPerMonth { get; private set; }
        public LoanStatus Status { get; private set; }
        public string? LoanType { get; private set; }
        public string? Currency { get; private set; }
        public DateTime Created { get; set; }

        public void Mapping(Profile Profile)
        {
            Profile.CreateMap<LoanApplication, LoanApplicationDto>()
                .ForMember(m => m.LoanType, o => o.MapFrom<LoanTypeNameResolver>())
                .ForMember(m => m.Currency, o => o.MapFrom<CurrencyNameResolver>());
        }

        private class LoanTypeNameResolver : IValueResolver<LoanApplication, LoanApplicationDto, string>
        {
            public string Resolve(LoanApplication source, LoanApplicationDto destination, string destMember, ResolutionContext context)
            {
                return source.LoanType.Name;
            }
        }

        private class CurrencyNameResolver : IValueResolver<LoanApplication, LoanApplicationDto, string>
        {
            public string Resolve(LoanApplication source, LoanApplicationDto destination, string destMember, ResolutionContext context)
            {
                return source.Currency.Name;
            }
        }
    }
}
