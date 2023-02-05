using Domain.Common;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
