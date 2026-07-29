using Domain.Common;
using Domain.Entities;

namespace Domain.Events
{
    public class OtpVerifiedEvent : BaseEvent
    {
        public OtpVerifiedEvent(OtpVerification verification)
        {
            Verification = verification;
        }
        public OtpVerification Verification { get; set; }
    }
}
