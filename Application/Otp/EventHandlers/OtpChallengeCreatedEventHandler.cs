using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Otp.EventHandlers
{
    /// <summary>
    /// Logging only. The message is sent by <c>IOtpService</c> after the challenge is persisted,
    /// not from here: domain events are dispatched before <c>SaveChangesAsync</c> commits, so
    /// sending here would text a code for a challenge a failed save never stored.
    /// </summary>
    public class OtpChallengeCreatedEventHandler : INotificationHandler<OtpChallengeCreatedEvent>
    {
        private readonly ILogger<OtpChallengeCreatedEventHandler> _logger;

        public OtpChallengeCreatedEventHandler(ILogger<OtpChallengeCreatedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(OtpChallengeCreatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Domain Event: {DomainEvent} {ChallengeId} {OtpPurpose}",
                notification.GetType().Name,
                notification.Verification.ChallengeId,
                notification.Verification.Purpose);

            return Task.CompletedTask;
        }
    }
}
