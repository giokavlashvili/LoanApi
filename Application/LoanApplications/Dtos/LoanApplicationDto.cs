using Application.Common.Mappings;
using AutoMapper;
using Domain.Entities;
using Domain.Enums;

namespace Application.LoanApplications.Dtos
{
    public class LoanApplicationDto : IMapFrom<LoanApplication>
    {
        // Public setters throughout, deliberately. ProjectTo builds a member-init expression
        // (new LoanApplicationDto { Amount = …, … }) which cannot assign an inaccessible setter;
        // in-memory IMapper.Map got away with private setters because it assigns by reflection.
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public int PeriodPerMonth { get; set; }
        public LoanStatus Status { get; set; }
        public string? LoanType { get; set; }
        public string? Currency { get; set; }
        public DateTime Created { get; set; }

        public void Mapping(Profile Profile)
        {
            Profile.CreateMap<LoanApplication, LoanApplicationDto>()
                .ForMember(m => m.LoanType, o => o.MapFrom(s => s.LoanType != null && s.LoanType.Name != null ? s.LoanType.Name : ""))
                .ForMember(m => m.Currency, o => o.MapFrom(s => s.Currency != null && s.Currency.Name != null ? s.Currency.Name : ""));
        }
    }
}
