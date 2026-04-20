namespace SkeletonApi.Application.Interfaces;

public class SseEvent
{
    public string Type { get; set; } = string.Empty;
    public object Data { get; set; } = new();
}

public interface ISseManager
{
    Task PublishToRedisAsync(SseEvent sseEvent);
    Task SubscribeToRedisAsync(CancellationToken cancellationToken);
    Task AddClientAsync(string clientId, Func<SseEvent, Task> sendAsync, CancellationToken cancellationToken);
}
