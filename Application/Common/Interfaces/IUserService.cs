using Application.Common.Models;

namespace Application.Common.Interfaces
{
    /// <summary>
    /// The read-only half of the identity contract: look a user up, name them, check a role.
    /// <see cref="IIdentityService"/> extends it with the lifecycle and authentication operations.
    /// </summary>
    /// <remarks>
    /// Lives here, not in <c>Domain</c>. This is an outbound contract to a service the application
    /// consumes, and declaring it in the domain had the innermost layer describing a dependency it
    /// does not have — <c>Domain</c> never called it.
    /// </remarks>
    public interface IUserService
    {
        Task<User?> GetUserByIdAsync(string id);

        Task<string?> GetUserNameAsync(string userId);

        Task<bool> IsInRoleAsync(string userId, string role);
    }
}
