namespace SkeletonApi.Application.Interfaces;

public interface ICacheRepository
{
    Task<T?> GetAsync<T>(string key);
    Task<T?> GetFromReplicaAsync<T>(string key); // Read from replica if available
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
}
