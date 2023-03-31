using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class LoanType : BaseEntity
    {
        public string? Name { get; private set; }

        public static LoanType Create(string name)
        {
            return new LoanType()
            {
                Name = name
            };
        }
    }
}
