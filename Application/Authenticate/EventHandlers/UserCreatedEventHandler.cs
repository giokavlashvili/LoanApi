using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Authenticate.EventHandlers
{
    public class UserCreatedEventHandler : INotificationHandler<UserCreatedEvent>
    {
        private readonly ILogger<UserCreatedEventHandler> _logger;

        public UserCreatedEventHandler(ILogger<UserCreatedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent} {UserName}", notification.GetType().Name, notification.UserName);

            return Task.CompletedTask;
        }
    }
}
