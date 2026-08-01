using Application.Otp.Dtos;

namespace Application.Operations.Dtos
{
    /// <summary>
    /// What <c>initiate</c> returns: the handle needed to confirm, plus the challenge details the
    /// user needs to recognise the message. Nothing here helps someone who did not receive it.
    /// </summary>
    public class PendingOperationDto
    {
        /// <summary>The handle <c>confirm</c> and <c>resend</c> are addressed to.</summary>
        public Guid OperationId { get; set; }

        public Guid ChallengeId { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>Masked, e.g. <c>*********3456</c>.</summary>
        public string? Recipient { get; set; }

        public int MaxAttempts { get; set; }

        public static PendingOperationDto From(Guid operationId, OtpChallengeDto challenge) => new()
        {
            OperationId = operationId,
            ChallengeId = challenge.ChallengeId,
            ExpiresAt = challenge.ExpiresAt,
            Recipient = challenge.Recipient,
            MaxAttempts = challenge.MaxAttempts
        };
    }
}
