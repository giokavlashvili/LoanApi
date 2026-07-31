using Application.Common.Confirmation;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Otp.Dtos;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.Confirmation.Services
{
    /// <inheritdoc cref="IOperationConfirmationService"/>
    public class OperationConfirmationService : IOperationConfirmationService
    {
        private readonly IOtpService _otpService;
        private readonly IOtpCodeHasher _codeHasher;
        private readonly ICurrentUserService _currentUserService;
        private readonly IUserService _userService;

        public OperationConfirmationService(
            IOtpService otpService,
            IOtpCodeHasher codeHasher,
            ICurrentUserService currentUserService,
            IUserService userService)
        {
            _otpService = otpService;
            _codeHasher = codeHasher;
            _currentUserService = currentUserService;
            _userService = userService;
        }

        public async Task<OtpChallengeDto> IssueChallengeAsync(
            PendingOperation operation,
            OperationTypeDescriptor descriptor,
            CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.RequireUserId();

            EnsureMayConfirm(operation, descriptor, userId);

            var user = await _userService.GetUserByIdAsync(userId);

            if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
                throw new DomainValidationException("OtpRecipientRequired");

            // Purpose is the discriminator alone, never the operation id. It doubles as the
            // throttle key for MaxPerRecipientPerHour and ResendCooldown, so folding the operation
            // id in would give every operation its own bucket and quietly disable the SMS rate
            // limit. The cost is that only one code per operation type is live at a time; the
            // binding that actually matters is the request hash below.
            return await _otpService.IssueAsync(
                descriptor.Discriminator,
                user.PhoneNumber,
                userId,
                ComputeRequestHash(operation.OperationId, descriptor.Discriminator),
                cancellationToken);
        }

        public void EnsureMayConfirm(PendingOperation operation, OperationTypeDescriptor descriptor, string userId)
        {
            switch (descriptor.Policy)
            {
                case ConfirmationPolicy.SameUser
                    when !string.Equals(operation.InitiatedBy, userId, StringComparison.Ordinal):
                    throw new DomainValidationException("PendingOperationWrongConfirmer");

                case ConfirmationPolicy.DifferentUser
                    when string.Equals(operation.InitiatedBy, userId, StringComparison.Ordinal):
                    throw new DomainValidationException("PendingOperationSelfConfirm");
            }
        }

        public string ComputeRequestHash(Guid operationId, string discriminator) =>
            _codeHasher.Hash(operationId, discriminator);
    }
}
