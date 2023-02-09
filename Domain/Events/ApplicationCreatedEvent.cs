using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class ApplicationCreatedEvent : BaseEvent
    {
        public ApplicationCreatedEvent(LoanApplication aplication)
        {
            Aplication = aplication;
        }
        public LoanApplication Aplication { get; set; }
    }
}
