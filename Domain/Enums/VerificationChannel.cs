namespace Domain.Enums
{
    /// <summary>
    /// How a one time password reaches its recipient.
    /// <para>
    /// Only <see cref="Sms"/> is implemented. <see cref="Email"/> is declared so the column and the
    /// sender abstraction do not need changing when it lands, but nothing resolves an email sender
    /// today and no email recipient can be looked up — <c>Application.Common.Models.User</c> has no
    /// <c>Email</c>, and the account model deliberately does not carry one. See phase 6 task 1.
    /// </para>
    /// </summary>
    public enum VerificationChannel
    {
        Sms = 0,
        Email = 1
    }
}
