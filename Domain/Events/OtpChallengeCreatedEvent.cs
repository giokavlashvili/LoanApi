using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class OtpChallengeCreatedEvent : BaseEvent
    {
        public OtpChallengeCreatedEvent(OtpVerification verification)
        {
            Verification = verification;
        }
        public OtpVerification Verification { get; set; }
    }
}
