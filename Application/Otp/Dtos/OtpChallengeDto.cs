namespace Application.Otp.Dtos
{
    /// <summary>
    /// What the caller gets back when an operation needs confirmation: enough to answer the
    /// challenge, and nothing that would help someone who did not receive the message.
    /// </summary>
    public class OtpChallengeDto
    {
        public Guid ChallengeId { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Masked recipient, e.g. <c>+9955****678</c>. Enough for the user to recognise which
        /// number was texted, not enough to disclose it to someone who does not already know it.
        /// </summary>
        public string? Recipient { get; set; }

        /// <summary>Wrong guesses allowed before the challenge locks.</summary>
        public int MaxAttempts { get; set; }
    }
}
