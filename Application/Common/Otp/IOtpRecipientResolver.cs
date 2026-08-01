namespace Application.Common.Otp
{
    /// <summary>
    /// Decides where a code is sent. One implementation, shared by every mechanism that issues
    /// one — this is the rule that stops a caller redirecting their own confirmation code to a
    /// phone they control, and a second copy of it would eventually disagree with the first.
    /// </summary>
    public interface IOtpRecipientResolver
    {
        /// <summary>
        /// Returns <paramref name="suppliedRecipient"/> when the caller is permitted to name one,
        /// otherwise the authenticated user's number.
        /// </summary>
        /// <param name="suppliedRecipient">
        /// A recipient carried by the request. Null everywhere except flows where no account
        /// exists yet to read a number from — registration being the only current example.
        /// </param>
        Task<string> ResolveAsync(string? suppliedRecipient, CancellationToken cancellationToken = default);
    }
}
