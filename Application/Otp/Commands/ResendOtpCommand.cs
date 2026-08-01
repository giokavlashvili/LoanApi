using Application.Common.Interfaces;
using Application.Otp.Dtos;
using MediatR;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Application.Otp.Commands
{
    /// <summary>
    /// Re-issues a code for a challenge the caller already holds. Deliberately not an
    /// <see cref="Common.Otp.IRequireOtpVerification"/> command — it is the way out of a lost
    /// message, so requiring a code to get a code would deadlock the flow.
    /// <para>
    /// <see cref="ISkipTransaction"/> because <c>IOtpService.IssueAsync</c> persists the challenge
    /// and then sends the SMS, relying on that save having committed. Reached from inside this
    /// handler the save would only flush, so a commit failure afterwards would roll the challenge
    /// back with the message already gone — leaving a code that can never be verified. The gated
    /// commands avoid this by having <c>OtpVerificationBehavior</c> sit outside the transaction;
    /// this one issues from the handler, so it has to opt out here.
    /// </para>
    /// </summary>
    public class ResendOtpCommand : IRequest<OtpChallengeDto>, ISkipTransaction
    {
        public Guid? ChallengeId { get; set; }
    }

    public class ResendOtpCommandHandler : IRequestHandler<ResendOtpCommand, OtpChallengeDto>
    {
        private readonly IOtpService _otpService;

        public ResendOtpCommandHandler(IOtpService otpService)
        {
            _otpService = otpService;
        }

        public async Task<OtpChallengeDto> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
        {
            return await _otpService.ResendAsync(request.ChallengeId.Value, cancellationToken);
        }
    }
}
