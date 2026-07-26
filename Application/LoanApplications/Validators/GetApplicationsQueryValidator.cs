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

        public GetApplicationsQueryValidator(IStringLocalizer stringLocalizer, IOptions<PaginationOptions> paginationOptions)
        {
            _stringLocalizer = stringLocalizer;

            RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1)
                .WithMessage(_stringLocalizer.GetString("InvalidPageNumber"));

            RuleFor(q => q.PageSize).InclusiveBetween(1, paginationOptions.Value.MaxPageSize)
                .WithMessage(_stringLocalizer.GetString("InvalidPageSize"));
        }
    }
}
