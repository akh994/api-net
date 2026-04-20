namespace SkeletonApi.Common.Messaging.Abstractions;

/// <summary>
/// Message broker client interface for publishing and subscribing to messages
/// </summary>
public interface IMessageClient : IDisposable
{
    /// <summary>
    /// Publish a message to a topic
    /// </summary>
    Task PublishAsync(string topic, byte[] message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish a message with metadata/headers
    /// </summary>
    Task PublishAsync(string topic, byte[] message, IDictionary<string, string> headers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish multiple messages in bulk
    /// </summary>
    Task BulkPublishAsync(string topic, IEnumerable<byte[]> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to a topic with a subscription name
    /// </summary>
    Task<ISubscriptionHandler> SubscribeAsync(
        string topic,
        string subscription,
        Func<MessageContext, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe with configuration options
    /// </summary>
    Task<ISubscriptionHandler> SubscribeAsync(
        string topic,
        string subscription,
        Func<MessageContext, Task> handler,
        SubscriptionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Subscription handler for managing active subscriptions
/// </summary>
public interface ISubscriptionHandler : IDisposable
{
    string Topic { get; }
    string Subscription { get; }
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Message context containing message data and metadata
/// </summary>
public class MessageContext
{
    public string Topic { get; set; } = string.Empty;
    public string Subscription { get; set; } = string.Empty;
    public byte[] Body { get; set; } = Array.Empty<byte>();
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Acknowledge the message (mark as successfully processed)
    /// </summary>
    public Func<Task>? Ack { get; set; }

    /// <summary>
    /// Negative acknowledge (reject and optionally requeue)
    /// </summary>
    public Func<bool, Task>? Nack { get; set; }
}

/// <summary>
/// Subscription configuration options
/// </summary>
public class SubscriptionOptions
{
    /// <summary>
    /// Number of concurrent message handlers
    /// </summary>
    public int ConcurrentHandlers { get; set; } = 1;

    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Enable dead letter queue
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Message TTL in milliseconds
    /// </summary>
    public int MessageTtlMs { get; set; } = 7 * 24 * 60 * 60 * 1000; // 7 days

    /// <summary>
    /// Prefetch count for message batching
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;
}
