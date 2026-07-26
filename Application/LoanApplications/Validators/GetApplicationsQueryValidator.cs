using Application.Common.Models;
using Application.LoanApplications.Queries;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Application.LoanApplications.Validators
{
    public class GetApplicationsQueryValidator : AbstractValidator<GetApplicationsQuery>
    {
        private readonly IStringLocalizer _stringLocalizer;

        public GetApplicationsQueryValidator(IStringLocalizer stringLocalizer, IOptionsMonitor<PaginationOptions> paginationOptions)
        {
            _stringLocalizer = stringLocalizer;

            RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1)
                .WithMessage(_stringLocalizer.GetString("InvalidPageNumber"));

            // Read CurrentValue inside the predicate, not the constructor, so a config reload
            // takes effect immediately regardless of how long this validator instance lives.
            RuleFor(q => q.PageSize).Must(pageSize => pageSize >= 1 && pageSize <= paginationOptions.CurrentValue.MaxPageSize)
                .WithMessage(_stringLocalizer.GetString("InvalidPageSize"));
        }
    }
}
