using Domain.Enums;

namespace Application.LoanApplications.Dtos
{
    public class LoanApplicationDto
    {
        // Public setters throughout. The query handler projects with a member-init expression
        // (new LoanApplicationDto { Amount = …, … }), which EF translates to the SELECT list and
        // which cannot assign an inaccessible setter.
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public int PeriodPerMonth { get; set; }
        public LoanStatus Status { get; set; }

        /// <summary>Flattened from <c>LoanApplication.LoanType.Name</c>; "" when the navigation is absent.</summary>
        public string? LoanType { get; set; }

        /// <inheritdoc cref="LoanType"/>
        public string? Currency { get; set; }

        public DateTime Created { get; set; }
    }
}
