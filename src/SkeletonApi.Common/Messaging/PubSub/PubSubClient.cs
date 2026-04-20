using System.Text;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Auth;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Common.Messaging.PubSub;

public class PubSubClient : IMessageClient
{
    private readonly ILogger<PubSubClient> _logger;
    private readonly PubSubOptions _options;
    private readonly MessagingConnectionManager _connectionManager;
    private readonly List<SubscriberClient> _subscribers = new();
    private bool _disposed;

    public PubSubClient(PubSubOptions options, ILogger<PubSubClient> logger, MessagingConnectionManager connectionManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task PublishAsync(string topic, byte[] message, CancellationToken cancellationToken = default)
    {
        await PublishAsync(topic, message, new Dictionary<string, string>(), cancellationToken);
    }

    public async Task PublishAsync(string topic, byte[] message, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

        // Auto-provision topic if enabled (we'll assume topic creation is usually desired if publishing)
        await EnsureTopicAsync(topic, cancellationToken);

        var publisher = await _connectionManager.GetPubSubPublisherAsync(_options, topic);

        var pubsubMessage = new PubsubMessage
        {
            Data = ByteString.CopyFrom(message)
        };

        foreach (var header in headers)
        {
            pubsubMessage.Attributes[header.Key] = header.Value;
        }

        var messageId = await publisher.PublishAsync(pubsubMessage);
        _logger.LogDebug("Published message to Pub/Sub topic: {Topic}, MessageId: {MessageId}", topic, messageId);
    }

    public async Task BulkPublishAsync(string topic, IEnumerable<byte[]> messages, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

        await EnsureTopicAsync(topic, cancellationToken);

        var publisher = await _connectionManager.GetPubSubPublisherAsync(_options, topic);
        cancellationToken.ThrowIfCancellationRequested();

        var pubsubMessages = messages.Select(msg => new PubsubMessage
        {
            Data = ByteString.CopyFrom(msg)
        }).ToList();

        var tasks = pubsubMessages.Select(msg => publisher.PublishAsync(msg));
        await Task.WhenAll(tasks);
    }

    public async Task<ISubscriptionHandler> SubscribeAsync(
        string topic,
        string subscription,
        Func<MessageContext, Task> handler,
        CancellationToken cancellationToken = default)
    {
        return await SubscribeAsync(topic, subscription, handler, new SubscriptionOptions(), cancellationToken);
    }

    public async Task<ISubscriptionHandler> SubscribeAsync(
        string topic,
        string subscription,
        Func<MessageContext, Task> handler,
        SubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (_options.CreateSubscription)
        {
            await EnsureSubscriptionAsync(topic, subscription, cancellationToken);
        }

        var subscriptionName = new SubscriptionName(_options.ProjectId, subscription);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var builder = new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            CredentialsPath = !string.IsNullOrEmpty(_options.Credentials) ? _options.Credentials : null,
            Settings = new SubscriberClient.Settings
            {
                FlowControlSettings = new Google.Api.Gax.FlowControlSettings(
                    maxOutstandingElementCount: options.ConcurrentHandlers > 0 ? options.ConcurrentHandlers : null,
                    maxOutstandingByteCount: null
                )
            }
        };

        var subscriber = await builder.BuildAsync(cts.Token);
        _subscribers.Add(subscriber);

        var subscriberTask = subscriber.StartAsync(async (pubsubMessage, ct) =>
        {
            try
            {
                var msgHeaders = new Dictionary<string, string>();
                if (pubsubMessage.Attributes != null)
                {
                    foreach (var attr in pubsubMessage.Attributes)
                    {
                        msgHeaders[attr.Key] = attr.Value;
                    }
                }

                var reply = SubscriberClient.Reply.Ack;

                var context = new MessageContext
                {
                    Topic = topic,
                    Subscription = subscription,
                    Body = pubsubMessage.Data.ToByteArray(),
                    Headers = msgHeaders,
                    MessageId = pubsubMessage.MessageId,
                    Timestamp = pubsubMessage.PublishTime.ToDateTime()
                };

                context.Ack = async () =>
                {
                    reply = SubscriberClient.Reply.Ack;
                    context.Ack = null;
                    await Task.CompletedTask;
                };

                context.Nack = async (requeue) =>
                {
                    reply = SubscriberClient.Reply.Nack;
                    context.Ack = null;
                    await Task.CompletedTask;
                };

                await handler(context);
                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Pub/Sub message from {Topic}/{Subscription}", topic, subscription);
                return SubscriberClient.Reply.Nack;
            }
        });

        return new PubSubSubscriptionHandler(topic, subscription, subscriber, cts, _logger, subscriberTask);
    }

    private async Task EnsureTopicAsync(string topic, CancellationToken ct)
    {
        try
        {
            var admin = await _connectionManager.GetPubSubTopicAdminClientAsync(_options);
            var topicName = new TopicName(_options.ProjectId, topic);

            try
            {
                await admin.GetTopicAsync(topicName);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                _logger.LogInformation("Pub/Sub Topic {Topic} not found, creating...", topic);
                await admin.CreateTopicAsync(topicName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Pub/Sub topic {Topic} exists. Infrastructure might already exist or permissions are missing.", topic);
        }
    }

    private async Task EnsureSubscriptionAsync(string topic, string subscription, CancellationToken ct)
    {
        try
        {
            await EnsureTopicAsync(topic, ct);

            var admin = await _connectionManager.GetPubSubSubscriptionAdminClientAsync(_options);
            var subName = new SubscriptionName(_options.ProjectId, subscription);
            var topicName = new TopicName(_options.ProjectId, topic);

            try
            {
                await admin.GetSubscriptionAsync(subName);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                _logger.LogInformation("Pub/Sub Subscription {Subscription} not found for topic {Topic}, creating...", subscription, topic);
                await admin.CreateSubscriptionAsync(subName, topicName, pushConfig: null, ackDeadlineSeconds: 60, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Pub/Sub subscription {Subscription} exists. Infrastructure might already exist or permissions are missing.", subscription);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var subscriber in _subscribers)
        {
            try
            {
                subscriber?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping Pub/Sub subscriber during disposal");
            }
        }
        _subscribers.Clear();

        // Shared publishers are managed by MessagingConnectionManager

        _disposed = true;
        _logger.LogInformation("Pub/Sub client disposed");
    }
}

internal class PubSubSubscriptionHandler : ISubscriptionHandler
{
    private readonly SubscriberClient _subscriber;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;
    private readonly Task _subscriberTask;

    public string Topic { get; }
    public string Subscription { get; }

    public PubSubSubscriptionHandler(
        string topic,
        string subscription,
        SubscriberClient subscriber,
        CancellationTokenSource cts,
        ILogger logger,
        Task subscriberTask)
    {
        Topic = topic;
        Subscription = subscription;
        _subscriber = subscriber;
        _cts = cts;
        _logger = logger;
        _subscriberTask = subscriberTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Pub/Sub subscription: {Topic}/{Subscription}...", Topic, Subscription);
        _cts.Cancel();

        try
        {
            await _subscriber.StopAsync(cancellationToken);
            if (_subscriberTask != null)
            {
                await _subscriberTask;
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping Pub/Sub subscriber");
        }

        _logger.LogInformation("Stopped Pub/Sub subscription: {Topic}/{Subscription}", Topic, Subscription);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
