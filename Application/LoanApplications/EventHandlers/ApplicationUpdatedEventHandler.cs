using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.LoanApplications.EventHandlers
{
    public class ApplicationUpdatedEventHandler : INotificationHandler<ApplicationUpdatedEvent>
    {
        private readonly ILogger<ApplicationUpdatedEventHandler> _logger;

        public ApplicationUpdatedEventHandler(ILogger<ApplicationUpdatedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(ApplicationUpdatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent} ", notification.GetType().Name);

            return Task.CompletedTask;
        }
    }
}
