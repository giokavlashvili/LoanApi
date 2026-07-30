using MediatR;

namespace Application.Authenticate.Notifications
{
    /// <summary>
    /// Raised once ASP.NET Identity has actually created an account.
    /// <para>
    /// This is an <b>integration style notification, not a domain event</b>. It deliberately does
    /// not derive from <c>BaseEvent</c> and is never accumulated on
    /// <c>BaseEntity.DomainEvents</c> — it is published directly through <c>IMediator</c> by
    /// <c>IdentityService</c>.
    /// </para>
    /// <para>
    /// The domain event path is closed to <c>ApplicationUser</c> at the type level, not by choice:
    /// <c>MediatorExtensions.DispatchDomainEvents</c> scans
    /// <c>ChangeTracker.Entries&lt;BaseEntity&gt;()</c>, and an <c>IdentityUser</c> can never be a
    /// <c>BaseEntity</c> — Identity's stores require that base, and the two disagree about the key
    /// type besides (<c>int</c> against <c>string</c>). The save path is not the obstacle: Identity
    /// persists through the same <c>ApplicationDbContext</c>, so dispatch does run during
    /// registration; it simply finds nothing to publish.
    /// </para>
    /// <para>
    /// Its predecessor <c>UserCreatedEvent : BaseEvent</c> was typed as a domain event, which
    /// implied it travelled the aggregate path when nothing ever attached it to an entity. If the
    /// product ever needs a real domain <c>User</c> aggregate, this is the notification that should
    /// move onto it.
    /// </para>
    /// </summary>
    public sealed record UserRegisteredNotification(
        string UserName,
        string? FirstName,
        string? LastName) : INotification;
}
