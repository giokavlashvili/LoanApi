using Application.Otp.Dtos;

namespace Application.Common.Exceptions
{
    /// <summary>
    /// Raised when a confirmable command has been captured and parked. By the time this is thrown
    /// the operation exists and, when the policy names the initiator as the confirmer, a code has
    /// been texted; the caller follows up on <c>ConfirmOperationCommand</c> rather than re-sending
    /// the original request.
    /// <para>
    /// Signalled as an exception for the same reason as <see cref="OtpRequiredException"/>: a
    /// pipeline behaviour cannot synthesize a <c>TResponse</c>, and a command opting in must not
    /// have to change its signature. Like <see cref="ValidationException"/> it is an expected
    /// outcome, not a fault.
    /// </para>
    /// <para>
    /// It maps to <b>202 Accepted</b>, not the 428 the inline gate uses. 428 means "re-send this
    /// same request with the missing precondition", which is precisely the client-replay
    /// behaviour this topology exists to avoid — returning it here would train callers to hold
    /// and resend the payload.
    /// </para>
    /// </summary>
    public class OperationConfirmationRequiredException : Exception
    {
        public OperationConfirmationRequiredException(Guid operationId, OtpChallengeDto? challenge)
            : base("OperationConfirmationRequired")
        {
            OperationId = operationId;
            Challenge = challenge;
        }

        public Guid OperationId { get; }

        /// <summary>
        /// Null when no code was issued, which is the case whenever the confirmer is somebody
        /// other than the initiator: at initiate there is no way to know whose phone to text.
        /// That caller asks for a code themselves once they pick the operation up.
        /// </summary>
        public OtpChallengeDto? Challenge { get; }
    }
}
