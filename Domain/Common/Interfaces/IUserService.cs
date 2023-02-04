using Domain.Common.Models;

namespace Domain.Common.Interfaces
{
    public interface IUserService
    {
        Task<User?> GetUserByIdAsync(string id);

        Task<string?> GetUserNameAsync(string userId);

        Task<bool> IsInRoleAsync(string userId, string role);
    }
}
