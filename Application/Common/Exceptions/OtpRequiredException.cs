using Application.Otp.Dtos;

namespace Application.Common.Exceptions
{
    /// <summary>
    /// Raised when an operation needs phone confirmation and the request did not carry a code.
    /// A challenge has already been issued and texted by the time this is thrown; the caller
    /// re-sends the same request with <c>ChallengeId</c> and <c>OtpCode</c> filled in.
    /// <para>
    /// Signalling this as an exception is what keeps the mechanism free: a command opts in by
    /// implementing one interface and its signature never changes. Like
    /// <see cref="ValidationException"/>, it is an expected outcome rather than a fault.
    /// </para>
    /// </summary>
    public class OtpRequiredException : Exception
    {
        public OtpRequiredException(OtpChallengeDto challenge)
            : base("OtpRequired")
        {
            Challenge = challenge;
        }

        public OtpChallengeDto Challenge { get; }
    }
}
