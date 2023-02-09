using Domain.Common;

namespace Domain.Entities
{
    public class Currency : BaseEntity
    {
        public string? Name { get; private set; }

        public static Currency Create(string name)
        {
            return new Currency()
            {
                Name = name
            };
        }
    }
}
