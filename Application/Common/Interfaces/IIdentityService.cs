using Application.Common.Models;
using System.Security.Claims;

namespace Application.Common.Interfaces
{
    public interface IIdentityService
    {
        Task<string> GetUserNameAsync(string userId);

        Task<bool> IsInRoleAsync(string userId, string role);

        Task<bool> UserExistsAsync(string userName);

        Task<bool> AuthorizeAsync(string userId, string policyName);

        Task<bool> CreateUserAsync(string userName, string firstName, string lastName, string password, string personalNumber, DateTime? birthDate = null);

        Task<Result> DeleteUserAsync(string userId);

        Task<(string token, DateTime validTo)> AuthenticateAsync(string usernName, string password);
    }
}