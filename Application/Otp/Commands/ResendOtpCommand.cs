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
    /// </summary>
    public class ResendOtpCommand : IRequest<OtpChallengeDto>
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
