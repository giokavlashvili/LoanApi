namespace Application.Common.Interfaces
{
    /// <summary>
    /// Delivery of a text message. Kept separate from the OTP logic so swapping Twilio for
    /// Azure Communication Services is one class and one DI line, and so tests never send.
    /// </summary>
    public interface ISmsSender
    {
        Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
    }
}
