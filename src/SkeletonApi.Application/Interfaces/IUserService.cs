using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Application.Interfaces;

public interface IUserService
{
    Task<User> CreateAsync(User user);
    Task<User?> GetByIdAsync(string id);
    Task<IEnumerable<User>> GetAllAsync();
    Task<IEnumerable<User>> SearchAsync(string query);
    Task<(IEnumerable<User> Items, int TotalItems)> GetAllPaginatedAsync(int page, int pageSize);
    Task ProcessUserCreatedAsync(string userId);
    Task UpdateAsync(string id, User user);
    Task DeleteAsync(string id);
}
