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
    public class ApplicationCreatedEventHandler : INotificationHandler<ApplicationCreatedEvent>
    {
        private readonly ILogger<ApplicationCreatedEventHandler> _logger;

        public ApplicationCreatedEventHandler(ILogger<ApplicationCreatedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(ApplicationCreatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent} ", notification.GetType().Name);

            return Task.CompletedTask;
        }
    }
}
