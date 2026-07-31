using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class PendingOperationCreatedEvent : BaseEvent
    {
        public PendingOperationCreatedEvent(PendingOperation operation)
        {
            Operation = operation;
        }

        public PendingOperation Operation { get; set; }
    }
}
