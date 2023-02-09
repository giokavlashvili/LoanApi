using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.LoanApplications.EventHandlers
{
    public class ApplicationStatusChangedEventHandler : INotificationHandler<ApplicationStatusChangedEvent>
    {
        private readonly ILogger<ApplicationStatusChangedEventHandler> _logger;

        public ApplicationStatusChangedEventHandler(ILogger<ApplicationStatusChangedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(ApplicationStatusChangedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent} ", notification.GetType().Name);

            return Task.CompletedTask;
        }
    }
}
