using Application.Authenticate.EventHandlers;
using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.LoanApplications.EventHandlers
{
    public class ApplicationDeletedEventHandler : INotificationHandler<ApplicationDeletedEvent>
    {
        private readonly ILogger<ApplicationDeletedEventHandler> _logger;

        public ApplicationDeletedEventHandler(ILogger<ApplicationDeletedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(ApplicationDeletedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent} ", notification.GetType().Name);

            return Task.CompletedTask;
        }
    }
}
