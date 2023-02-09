using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class ApplicationUpdatedEvent : BaseEvent
    {
        public ApplicationUpdatedEvent(LoanApplication aplication)
        {
            Aplication = aplication;
        }
        public LoanApplication Aplication { get; set; }
    }
}
