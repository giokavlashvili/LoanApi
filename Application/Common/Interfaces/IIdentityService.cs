using Domain.Common.Interfaces;

namespace Application.Common.Interfaces
{
    public interface IIdentityService : IUserService
    {
        Task<bool> UserExistsAsync(string userName);

        Task<bool> AuthorizeAsync(string userId, string policyName);

        Task<bool> CreateUserAsync(string userName, string firstName, string lastName, string password, string personalNumber, DateTime? birthDate = null);

        Task<bool> DeleteUserAsync(string userId);

        Task<(string token, DateTime validTo)> AuthenticateAsync(string usernName, string password);
    }
}