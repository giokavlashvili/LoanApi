using Application.Otp.Dtos;

namespace Application.Common.Interfaces
{
    /// <summary>
    /// Issuing and redeeming one time passwords, independent of what they protect. Callers pass
    /// a purpose so a code is only ever good for the operation it was requested for.
    /// </summary>
    public interface IOtpService
    {
        /// <summary>
        /// Creates a challenge, texts the code, and retires any pending challenge for the same
        /// recipient and purpose so only one code is ever live.
        /// </summary>
        Task<OtpChallengeDto> IssueAsync(
            string purpose,
            string recipient,
            Domain.Enums.VerificationChannel channel,
            string? userId,
            string requestHash,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Issues a fresh code for the same operation, recipient and payload as an existing
        /// challenge. Returns a new challenge id — the old one is retired, so a code that was
        /// merely slow to arrive cannot still be used afterwards.
        /// </summary>
        Task<OtpChallengeDto> ResendAsync(Guid challengeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Consumes a challenge, or throws <see cref="Domain.Exceptions.DomainValidationException"/>
        /// with a localization key describing why it could not be.
        /// </summary>
        Task VerifyAsync(
            Guid challengeId,
            string purpose,
            string code,
            string requestHash,
            CancellationToken cancellationToken = default);
    }
}
