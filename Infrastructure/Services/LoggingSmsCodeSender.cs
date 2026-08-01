using Application.Common.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    /// <summary>
    /// Writes the message to the log instead of sending it, so the two step flow is runnable
    /// straight after cloning the template with no vendor account and nothing to configure.
    /// <para>
    /// <b>This is a development stand-in.</b> It logs live codes, which land in the Logs table.
    /// Replace it with a real <see cref="IVerificationCodeSender"/> — Twilio, Vonage, Azure
    /// Communication Services — before anything reaches production; it is one class and one line in
    /// <c>AddInfrastructureServices</c>. The warning on every send is deliberate: a template that
    /// silently swallowed messages in production would be worse than one that nags.
    /// </para>
    /// </summary>
    public class LoggingSmsCodeSender : IVerificationCodeSender
    {
        private readonly ILogger<LoggingSmsCodeSender> _logger;

        public LoggingSmsCodeSender(ILogger<LoggingSmsCodeSender> logger)
        {
            _logger = logger;
        }

        public VerificationChannel Channel => VerificationChannel.Sms;

        public Task SendAsync(string recipient, string message, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning(
                "No SMS provider is configured — the message for {Recipient} was not sent. Development only: {SmsMessage}",
                recipient,
                message);

            return Task.CompletedTask;
        }
    }
}
