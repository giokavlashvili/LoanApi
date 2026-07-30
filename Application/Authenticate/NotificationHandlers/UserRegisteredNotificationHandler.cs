using Application.Authenticate.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Authenticate.NotificationHandlers
{
    public class UserRegisteredNotificationHandler : INotificationHandler<UserRegisteredNotification>
    {
        private readonly ILogger<UserRegisteredNotificationHandler> _logger;

        public UserRegisteredNotificationHandler(ILogger<UserRegisteredNotificationHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(UserRegisteredNotification notification, CancellationToken cancellationToken)
        {
            // "Notification", not "Domain Event": this one is published directly by IdentityService
            // rather than dispatched from an aggregate. See UserRegisteredNotification.
            //
            // Not named {UserName} either — the Serilog SQL sink binds that property to the
            // Logs.UserName column reserved for the authenticated caller, and registration is
            // anonymous.
            _logger.LogInformation(
                "Notification: {Notification} {RegisteredUserName}",
                notification.GetType().Name,
                notification.UserName);

            return Task.CompletedTask;
        }
    }
}
