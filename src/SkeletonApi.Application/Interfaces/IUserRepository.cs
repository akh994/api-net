using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync();
    Task<IEnumerable<User>> SearchAsync(string query);
    Task<(IEnumerable<User> Items, int TotalItems)> GetAllPaginatedAsync(int page, int pageSize);
    Task CreateAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(string id);
}
