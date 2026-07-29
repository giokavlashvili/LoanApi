using Domain.Entities;

namespace Domain.Repositories
{
    public interface IOtpVerificationRepository : IRepository<OtpVerification>
    {
        Task<OtpVerification?> GetByChallengeIdAsync(Guid challengeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Challenges issued to one recipient for one purpose since <paramref name="since"/>.
        /// Backs the resend throttle, which is what stops the endpoint being used as a free
        /// SMS pump against an arbitrary number.
        /// </summary>
        Task<int> CountRecentAsync(string recipient, string purpose, DateTime since, CancellationToken cancellationToken = default);

        /// <summary>Most recently issued challenge for a recipient/purpose, or null.</summary>
        Task<OtpVerification?> GetLatestAsync(string recipient, string purpose, CancellationToken cancellationToken = default);
    }
}
