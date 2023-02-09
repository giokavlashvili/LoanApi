using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class ApplicationStatusChangedEvent : BaseEvent
    {
        public ApplicationStatusChangedEvent(LoanApplication aplication)
        {
            Aplication = aplication;
        }
        public LoanApplication Aplication { get; set; }
    }
}
