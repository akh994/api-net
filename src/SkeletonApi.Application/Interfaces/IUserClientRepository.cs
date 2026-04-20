using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Application.Interfaces;

public interface IUserGrpcRepository
{
    Task<User?> GetUserAsync(string id, CancellationToken cancellationToken = default);
}

public interface IUserRestRepository
{
    Task<User?> GetUserAsync(string id, CancellationToken cancellationToken = default);
}
