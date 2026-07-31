using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class PendingOperationConfirmedEvent : BaseEvent
    {
        public PendingOperationConfirmedEvent(PendingOperation operation)
        {
            Operation = operation;
        }

        public PendingOperation Operation { get; set; }
    }
}
