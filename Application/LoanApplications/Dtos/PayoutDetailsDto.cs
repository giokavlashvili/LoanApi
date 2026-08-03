namespace Application.LoanApplications.Dtos
{
    /// <summary>
    /// What <c>SubmitPayoutDetails</c> returns once confirmed.
    /// <para>
    /// Carries a <em>masked</em> card number rather than the real one. Verified-operation results
    /// are deliberately not encrypted — they are returned to the caller and captured in the response
    /// log, so encrypting the stored copy would protect a value that has already left. The rule is
    /// to not return the secret at all, and this DTO is what that looks like.
    /// </para>
    /// </summary>
    public class PayoutDetailsDto
    {
        public int ApplicationId { get; set; }

        /// <summary>e.g. <c>**** **** **** 4321</c>. Enough to confirm the right card, useless alone.</summary>
        public string? MaskedCardNumber { get; set; }

        public string? BankName { get; set; }
    }
}
