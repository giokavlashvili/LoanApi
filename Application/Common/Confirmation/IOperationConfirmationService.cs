using Application.Otp.Dtos;
using Domain.Entities;

namespace Application.Common.Confirmation
{
    /// <summary>
    /// The two decisions shared by everything that touches a parked operation: who is allowed to
    /// confirm it, and how a code is bound to it. Both the initiate behaviour and the confirm and
    /// challenge handlers need them, and they must not drift apart between the three.
    /// </summary>
    public interface IOperationConfirmationService
    {
        /// <summary>
        /// Texts a fresh code to the <b>current</b> user for this operation, after checking they
        /// are allowed to confirm it. Issuing to whoever is asking is what makes four eyes work:
        /// at initiate there is no way to know which approver will pick the operation up.
        /// </summary>
        Task<OtpChallengeDto> IssueChallengeAsync(
            PendingOperation operation,
            OperationTypeDescriptor descriptor,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Throws <see cref="Domain.Exceptions.DomainValidationException"/> unless
        /// <paramref name="userId"/> may confirm this operation under the declared policy.
        /// </summary>
        void EnsureMayConfirm(PendingOperation operation, OperationTypeDescriptor descriptor, string userId);

        /// <summary>
        /// The value bound into a challenge so a code can only ever redeem the operation it was
        /// issued for. Recomputed identically at confirm time; a challenge belonging to a
        /// different operation therefore fails on the request hash.
        /// </summary>
        string ComputeRequestHash(Guid operationId, string discriminator);
    }
}
