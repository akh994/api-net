using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkeletonApi.Application.Interfaces;
using StackExchange.Redis;

namespace SkeletonApi.Infrastructure.SSE;

public class SseManager : ISseManager
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SseManager> _logger;
    private readonly ConcurrentDictionary<string, Func<SseEvent, Task>> _clients = new();
    private const string RedisChannel = "sse:events";

    public SseManager(IConnectionMultiplexer redis, ILogger<SseManager> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishToRedisAsync(SseEvent sseEvent)
    {
        try
        {
            var subscriber = _redis.GetSubscriber();
            var message = JsonSerializer.Serialize(sseEvent);
            await subscriber.PublishAsync(RedisChannel, message);
            _logger.LogInformation("Published SSE event to Redis: {Type}", sseEvent.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SSE event to Redis");
            // Fallback to local broadcast if Redis fails
            await BroadcastLocalAsync(sseEvent);
        }
    }

    public async Task SubscribeToRedisAsync(CancellationToken cancellationToken)
    {
        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel, async (channel, message) =>
        {
            try
            {
                var sseEvent = JsonSerializer.Deserialize<SseEvent>(message.ToString());
                if (sseEvent != null)
                {
                    await BroadcastLocalAsync(sseEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle Redis SSE message");
            }
        });

        _logger.LogInformation("Subscribed to Redis SSE channel: {Channel}", RedisChannel);
    }

    public async Task AddClientAsync(string clientId, Func<SseEvent, Task> sendAsync, CancellationToken cancellationToken)
    {
        _clients.TryAdd(clientId, sendAsync);
        _logger.LogInformation("SSE client connected: {ClientId}. Total clients: {Count}", clientId, _clients.Count);

        try
        {
            // Keep connection open until cancelled
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal disconnection
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            _logger.LogInformation("SSE client disconnected: {ClientId}. Total clients: {Count}", clientId, _clients.Count);
        }
    }

    private async Task BroadcastLocalAsync(SseEvent sseEvent)
    {
        _logger.LogInformation("Broadcasting SSE event locally to {Count} clients", _clients.Count);

        foreach (var client in _clients)
        {
            try
            {
                await client.Value(sseEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SSE event to client: {ClientId}", client.Key);
            }
        }
    }
}
