using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Otp;
using Domain.Enums;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Common.Behaviors
{
    /// <summary>
    /// Enforces two step verification for any request implementing
    /// <see cref="IRequireOtpVerification"/>. Requests that do not are passed straight through,
    /// so this costs one type check for everything else in the pipeline.
    /// <para>
    /// Registered after <see cref="ValidationBehavior{TRequest, TResponse}"/> for two reasons: a
    /// malformed command must not cost an SMS, and the
    /// <see cref="DomainValidationException"/>s raised here still need wrapping and localizing
    /// on the way out.
    /// </para>
    /// </summary>
    // "notnull" rather than "TRequest : IRequest<TResponse>" — see ValidationBehavior for why
    // the tighter constraint silently excluded every void command. It matters here too: an
    // OTP gated command that returns nothing is entirely normal.
    public class OtpVerificationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly IOtpService _otpService;
        private readonly IOtpCodeHasher _codeHasher;
        private readonly ICurrentUserService _currentUserService;
        private readonly IUserService _userService;
        private readonly ILogger<OtpVerificationBehavior<TRequest, TResponse>> _logger;

        public OtpVerificationBehavior(
            IOtpService otpService,
            IOtpCodeHasher codeHasher,
            ICurrentUserService currentUserService,
            IUserService userService,
            ILogger<OtpVerificationBehavior<TRequest, TResponse>> logger)
        {
            _otpService = otpService;
            _codeHasher = codeHasher;
            _currentUserService = currentUserService;
            _userService = userService;
            _logger = logger;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is not IRequireOtpVerification otpRequest)
                return await next();

            var purpose = otpRequest.OtpPurpose;
            var userId = _currentUserService.UserId;
            var recipient = await ResolveRecipientAsync(otpRequest, userId);

            // Computed from the request with the two OTP members removed, so the value is the
            // same on the call that asks for a code and the call that answers with one. Anything
            // else the caller changes in between produces a different hash and is refused.
            var requestHash = _codeHasher.HashRequest(request);

            if (otpRequest.ChallengeId is null || string.IsNullOrWhiteSpace(otpRequest.OtpCode))
            {
                // SMS is the only channel the gate offers. A command that needs another would be
                // choosing a delivery mechanism from inside its own contract, which is a decision
                // for the generic endpoints in phase 6, not for a marker interface.
                var challenge = await _otpService.IssueAsync(
                    purpose, recipient, VerificationChannel.Sms, userId, requestHash, cancellationToken);

                _logger.LogInformation(
                    "OTP challenge {ChallengeId} issued for {OtpPurpose}",
                    challenge.ChallengeId,
                    purpose);

                throw new OtpRequiredException(challenge);
            }

            await _otpService.VerifyAsync(otpRequest.ChallengeId.Value, purpose, otpRequest.OtpCode, requestHash, cancellationToken);

            return await next();
        }

        private async Task<string> ResolveRecipientAsync(IRequireOtpVerification otpRequest, string? userId)
        {
            // A command only supplies a number when there is no user to look one up from —
            // registration. Everywhere else the number comes from the account, which is what
            // stops a caller redirecting their own confirmation code to a phone they control.
            var recipient = otpRequest.OtpRecipient;

            if (!string.IsNullOrWhiteSpace(recipient))
                return recipient;

            if (string.IsNullOrWhiteSpace(userId))
                throw new DomainValidationException("OtpRecipientRequired");

            var user = await _userService.GetUserByIdAsync(userId);

            if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
                throw new DomainValidationException("OtpRecipientRequired");

            return user.PhoneNumber;
        }
    }
}
