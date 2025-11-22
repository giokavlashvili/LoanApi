using Microsoft.AspNetCore.Identity.UI.Services;

namespace Infrastructure.Services
{
    public class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // No-op implementation - emails are not sent
            // You can log this or implement actual email sending later
            return Task.CompletedTask;
        }
    }
}
