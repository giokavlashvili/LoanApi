using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity
{
    /// <summary>
    /// The ASP.NET Identity user, extended with the profile fields this template identifies people
    /// by.
    /// </summary>
    /// <remarks>
    /// It carried a hand-rolled copy of <c>BaseEntity</c>'s <c>DomainEvents</c> list and
    /// <c>AddDomainEvent</c>, which could never fire: <c>MediatorExtensions.DispatchDomainEvents</c>
    /// scans <c>ChangeTracker.Entries&lt;BaseEntity&gt;()</c>, and this type is an
    /// <see cref="IdentityUser"/>. Worse than useless — it looked like a working extension point, so
    /// a caller could queue an event and get silence. Events about the user are published as
    /// <c>UserRegisteredNotification</c> from <c>IdentityService</c> instead.
    /// <para>
    /// Making it a <c>BaseEntity</c> is not an option: Identity's stores require the
    /// <see cref="IdentityUser"/> base, and the two disagree about identity anyway —
    /// <c>BaseEntity.Id</c> is an <see cref="int"/>, <see cref="IdentityUser.Id"/> a
    /// <see cref="string"/>.
    /// </para>
    /// </remarks>
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PersonalNumber { get; set; }
        public DateTime? BirthDate { get; set; }
    }
}
