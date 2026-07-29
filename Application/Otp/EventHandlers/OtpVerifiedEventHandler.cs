using Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Otp.EventHandlers
{
    public class OtpVerifiedEventHandler : INotificationHandler<OtpVerifiedEvent>
    {
        private readonly ILogger<OtpVerifiedEventHandler> _logger;

        public OtpVerifiedEventHandler(ILogger<OtpVerifiedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(OtpVerifiedEvent notification, CancellationToken cancellationToken)
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
