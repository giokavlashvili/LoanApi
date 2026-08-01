using Domain.Enums;

namespace Application.Common.Interfaces
{
    /// <summary>
    /// Delivery of a verification code over one channel. Kept separate from the OTP logic so
    /// swapping a logging stand-in for Twilio or Azure Communication Services is one class and one
    /// DI line, and so tests never send.
    /// <para>
    /// Registered keyed by <see cref="Channel"/>. Only <see cref="VerificationChannel.Sms"/> is
    /// registered — resolving an unregistered channel throws, which is the intent: a channel that
    /// silently delivered nothing would be worse than one that fails.
    /// </para>
    /// </summary>
    public interface IVerificationCodeSender
    {
        VerificationChannel Channel { get; }

        Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default);
    }
}
