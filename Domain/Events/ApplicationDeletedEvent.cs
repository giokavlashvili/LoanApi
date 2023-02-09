using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class ApplicationDeletedEvent : BaseEvent
    {
        public ApplicationDeletedEvent(LoanApplication aplication)
        {
            Aplication = aplication;
        }
        public LoanApplication Aplication { get; set; }
    }
}
