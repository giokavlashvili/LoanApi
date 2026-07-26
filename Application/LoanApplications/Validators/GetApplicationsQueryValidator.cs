using Application.LoanApplications.Queries;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Application.LoanApplications.Validators
{
    public class GetApplicationsQueryValidator : AbstractValidator<GetApplicationsQuery>
    {
        public const int MaxPageSize = 100;

        private readonly IStringLocalizer _stringLocalizer;

        public GetApplicationsQueryValidator(IStringLocalizer stringLocalizer)
        {
            _stringLocalizer = stringLocalizer;

            RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1)
                .WithMessage(_stringLocalizer.GetString("InvalidPageNumber"));

            RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize)
                .WithMessage(_stringLocalizer.GetString("InvalidPageSize"));
        }
    }
}
