using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;
using MQTTnet;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Common.Messaging;

public class MqttClient : IMessageClient
{
    private readonly ILogger<MqttClient> _logger;
    private readonly MqttOptions _options;
    private readonly IMqttClient _client;
    private readonly List<MqttSubscriptionHandler> _handlers = new();
    private bool _disposed;

    public MqttClient(MqttOptions options, ILogger<MqttClient> logger, IMqttClient client)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task PublishAsync(string topic, byte[] message, CancellationToken cancellationToken = default)
    {
        await PublishAsync(topic, message, new Dictionary<string, string>(), cancellationToken);
    }

    public async Task PublishAsync(string topic, byte[] message, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected");
        }

        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(message)
            .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)_options.Qos)
            .Build();

        await _client.PublishAsync(mqttMessage, cancellationToken);
        _logger.LogDebug("Published message to MQTT topic: {Topic}", topic);
    }

    public async Task BulkPublishAsync(string topic, IEnumerable<byte[]> messages, CancellationToken cancellationToken = default)
    {
        foreach (var msg in messages)
        {
            await PublishAsync(topic, msg, cancellationToken);
        }
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
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected");
        }

        var topicFilter = new MqttTopicFilterBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)_options.Qos)
            .Build();

        await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter)
            .Build(), cancellationToken);

        var subscriptionHandler = new MqttSubscriptionHandler(topic, subscription, _client, _logger, handler, _options);
        _handlers.Add(subscriptionHandler);

        _logger.LogInformation("Subscribed to MQTT topic: {Topic} with subscription: {Subscription}", topic, subscription);

        return subscriptionHandler;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var handler in _handlers)
        {
            try { handler.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Error disposing MQTT subscription handler"); }
        }
        _handlers.Clear();

        _disposed = true;
        _logger.LogInformation("MQTT client disposed");
    }
}

internal class MqttSubscriptionHandler : ISubscriptionHandler
{
    private readonly IMqttClient _client;
    private readonly ILogger _logger;
    private readonly Func<MessageContext, Task> _handler;
    private readonly MqttOptions _options;
    private readonly string _topic;
    private readonly string _subscription;

    public string Topic => _topic;
    public string Subscription => _subscription;

    public MqttSubscriptionHandler(
        string topic,
        string subscription,
        IMqttClient client,
        ILogger logger,
        Func<MessageContext, Task> handler,
        MqttOptions options)
    {
        _topic = topic;
        _subscription = subscription;
        _client = client;
        _logger = logger;
        _handler = handler;
        _options = options;

        _client.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
    }

    private async Task HandleMessageReceivedAsync(global::MQTTnet.MqttApplicationMessageReceivedEventArgs e)
    {
        if (e.ApplicationMessage.Topic != _topic) return;

        try
        {
            var context = new MessageContext
            {
                Topic = _topic,
                Subscription = _subscription,
                Body = e.ApplicationMessage.Payload.ToArray(),
                Headers = new Dictionary<string, string>(),
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Ack = () => Task.CompletedTask,
                Nack = (requeue) => Task.CompletedTask
            };

            var maxRetries = _options.MaxRetries;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    await _handler(context);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == maxRetries) throw;
                    _logger.LogWarning(ex, "Error processing MQTT message (attempt {Attempt}/{MaxRetries}). Retrying...", i + 1, maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process MQTT message from topic {Topic}", _topic);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _client.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
        await _client.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(_topic)
            .Build(), cancellationToken);
    }

    public void Dispose()
    {
        _client.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
    }
}
