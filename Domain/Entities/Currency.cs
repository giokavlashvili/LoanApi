using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class Currency : BaseEntity
    {
        public string? Name { get; private set; }

        public static Currency Create(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new DomainException("Invalid currency name");

            return new Currency()
            {
                Name = name
            };
        }
    }
}
