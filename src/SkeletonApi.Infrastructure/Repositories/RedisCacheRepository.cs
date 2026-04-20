using System.Text.Json;
using Elastic.Apm.StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Errors;
using StackExchange.Redis;

namespace SkeletonApi.Infrastructure.Repositories;

public class RedisCacheRepository : ICacheRepository
{
    private readonly IConnectionMultiplexer _redisMaster;
    private readonly IConnectionMultiplexer _redisReplica;
    private readonly IDatabase _masterDatabase;
    private readonly IDatabase _replicaDatabase;

    public RedisCacheRepository(
        [FromKeyedServices("RedisMaster")] IConnectionMultiplexer redisMaster,
        [FromKeyedServices("RedisReplica")] IConnectionMultiplexer redisReplica)
    {
        _redisMaster = redisMaster;
        _redisReplica = redisReplica;

        _redisMaster.UseElasticApm();
        _redisReplica.UseElasticApm();

        _masterDatabase = _redisMaster.GetDatabase();
        _replicaDatabase = _redisReplica.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _masterDatabase.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>((string)value!);
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to get key {key} from Redis master", ex);
        }
    }

    public async Task<T?> GetFromReplicaAsync<T>(string key)
    {
        try
        {
            var value = await _replicaDatabase.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>((string)value!);
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to get key {key} from Redis replica", ex);
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _masterDatabase.KeyDeleteAsync(key);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            if (expiration.HasValue)
            {
                await _masterDatabase.StringSetAsync(key, json, expiration.Value, false, When.Always, CommandFlags.None);
            }
            else
            {
                await _masterDatabase.StringSetAsync(key, json, null, false, When.Always, CommandFlags.None);
            }
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to set key {key} in Redis", ex);
        }
    }
}
