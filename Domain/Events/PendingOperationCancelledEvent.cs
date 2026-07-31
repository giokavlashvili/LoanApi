using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class PendingOperationCancelledEvent : BaseEvent
    {
        public PendingOperationCancelledEvent(PendingOperation operation)
        {
            Operation = operation;
        }

        public PendingOperation Operation { get; set; }
    }
}
