using System.Text;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Common.Messaging.RabbitMQ;

public class RabbitMQClient : IMessageClient
{
    private readonly global::RabbitMQ.Client.IChannel _channel;
    private readonly SemaphoreSlim _channelLock = new SemaphoreSlim(1, 1);
    private readonly ILogger<RabbitMQClient> _logger;
    private readonly RabbitMQOptions _options;
    private bool _disposed;

    public RabbitMQClient(RabbitMQOptions options, ILogger<RabbitMQClient> logger, IConnectionPool connectionPool)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectionPool);

        _options = options;
        _logger = logger;

        var connection = connectionPool.GetConnection();
        _channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

        _logger.LogDebug("RabbitMQ Client initialized using connection pool");
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _channel?.CloseAsync().GetAwaiter().GetResult();
            _channel?.Dispose();
            _channelLock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing RabbitMQ channel");
        }

        _disposed = true;
        _logger.LogDebug("RabbitMQ channel disposed");
    }

    public async Task PublishAsync(string topic, byte[] message, CancellationToken cancellationToken = default)
    {
        await PublishAsync(topic, message, new Dictionary<string, string>(), cancellationToken);
    }

    // ActivitySource for distributed tracing
    private static readonly System.Diagnostics.ActivitySource ActivitySource = new("SkeletonApi.Common.Messaging.RabbitMQ");

    public async Task PublishAsync(string topic, byte[] message, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

        var span = Elastic.Apm.Agent.Tracer.CurrentTransaction?.StartSpan($"Publish {topic}", "messaging", "rabbitmq", "publish");

        try
        {
            // Declare exchange
            await _channel.ExchangeDeclareAsync(topic, ExchangeType.Fanout, durable: _options.ExchangeDurable, autoDelete: false, cancellationToken: cancellationToken);

            // Convert headers to RabbitMQ format
            var props = new global::RabbitMQ.Client.BasicProperties
            {
                DeliveryMode = global::RabbitMQ.Client.DeliveryModes.Persistent, // Always use persistent for reliability
                Headers = new Dictionary<string, object?>()
            };

            // Inject trace context
            if (span != null)
            {
                props.Headers["traceparent"] = span.OutgoingDistributedTracingData.SerializeToString();
            }
            else if (Elastic.Apm.Agent.Tracer.CurrentTransaction != null)
            {
                props.Headers["traceparent"] = Elastic.Apm.Agent.Tracer.CurrentTransaction.OutgoingDistributedTracingData.SerializeToString();
            }

            foreach (var header in headers)
            {
                props.Headers[header.Key] = header.Value;
            }

            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                // Publish message
                await _channel.BasicPublishAsync(
                    exchange: topic,
                    routingKey: "",
                    mandatory: false,
                    basicProperties: props,
                    body: message,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }

            _logger.LogDebug("Published message to topic: {Topic}", topic);
        }
        catch (Exception ex)
        {
            span?.CaptureException(ex);
            throw;
        }
        finally
        {
            span?.End();
        }
    }

    public async Task BulkPublishAsync(string topic, IEnumerable<byte[]> messages, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            await PublishAsync(topic, message, cancellationToken);
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
        // Declare exchange
        await _channel.ExchangeDeclareAsync(topic, ExchangeType.Fanout, durable: _options.ExchangeDurable, autoDelete: false, cancellationToken: cancellationToken);

        // Declare queue with DLQ support
        var queueArgs = new Dictionary<string, object?>();
        var enableDlq = options.EnableDeadLetterQueue || _options.EnableDlq;

        if (enableDlq)
        {
            var dlxName = $"{topic}-dlx";
            var dlqName = $"{subscription}-dlq";

            // Declare DLX and DLQ
            await _channel.ExchangeDeclareAsync(dlxName, ExchangeType.Direct, durable: _options.ExchangeDurable, cancellationToken: cancellationToken);
            await _channel.QueueDeclareAsync(dlqName, durable: _options.QueueDurable, exclusive: false, autoDelete: _options.QueueAutoDelete, cancellationToken: cancellationToken);
            await _channel.QueueBindAsync(dlqName, dlxName, subscription, cancellationToken: cancellationToken);

            queueArgs["x-dead-letter-exchange"] = dlxName;
            queueArgs["x-dead-letter-routing-key"] = subscription;
        }

        var messageTtl = options.MessageTtlMs > 0 ? options.MessageTtlMs : (_options.MessageTtl * 1000);
        if (messageTtl > 0)
        {
            queueArgs["x-message-ttl"] = messageTtl;
        }

        if (!string.IsNullOrEmpty(_options.QueueType))
        {
            queueArgs["x-queue-type"] = _options.QueueType;
        }

        var queueExpiration = _options.QueueExpiration * 1000;
        if (queueExpiration > 0)
        {
            queueArgs["x-expires"] = queueExpiration;
        }

        // Declare queue
        await _channel.QueueDeclareAsync(subscription, durable: _options.QueueDurable, exclusive: false, autoDelete: _options.QueueAutoDelete, arguments: queueArgs, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(subscription, topic, "", cancellationToken: cancellationToken);

        // Declare retry exchange with delayed message plugin
        var retryExchange = $"{topic}.retry";
        var retryArgs = new Dictionary<string, object?>
        {
            { "x-delayed-type", "direct" }
        };
        await _channel.ExchangeDeclareAsync(retryExchange, "x-delayed-message", durable: _options.ExchangeDurable, autoDelete: false, arguments: retryArgs, cancellationToken: cancellationToken);

        // Bind queue to retry exchange
        await _channel.QueueBindAsync(subscription, retryExchange, $"{subscription}.retry", cancellationToken: cancellationToken);

        // Set prefetch count
        await _channel.BasicQosAsync(0, options.PrefetchCount, false, cancellationToken);

        // Create consumer
        var consumer = new global::RabbitMQ.Client.Events.AsyncEventingBasicConsumer(_channel);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var semaphore = new SemaphoreSlim(options.ConcurrentHandlers > 0 ? options.ConcurrentHandlers : 1);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            await semaphore.WaitAsync();
            try
            {
                // Extract trace context
                Elastic.Apm.Api.DistributedTracingData? distributedTracingData = null;
                if (ea.BasicProperties.Headers != null &&
                    ea.BasicProperties.Headers.TryGetValue("traceparent", out var tpObj) &&
                    tpObj is byte[] tpBytes)
                {
                    var traceParent = Encoding.UTF8.GetString(tpBytes);
                    distributedTracingData = Elastic.Apm.Api.DistributedTracingData.TryDeserializeFromString(traceParent);
                }

                // Start transaction
                var transaction = Elastic.Apm.Agent.Tracer.StartTransaction($"Process {topic}", "messaging", distributedTracingData);
                transaction.SetLabel("subscription", subscription);
                transaction.SetLabel("topic", topic);

                try
                {
                    var msgHeaders = new Dictionary<string, string>();
                    if (ea.BasicProperties.Headers != null)
                    {
                        foreach (var header in ea.BasicProperties.Headers)
                        {
                            if (header.Value is byte[] bytes)
                            {
                                msgHeaders[header.Key] = Encoding.UTF8.GetString(bytes);
                            }
                        }
                    }

                    var context = new MessageContext
                    {
                        Topic = topic,
                        Subscription = subscription,
                        Body = ea.Body.ToArray(),
                        Headers = msgHeaders,
                        MessageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString(),
                        Ack = async () =>
                        {
                            await _channelLock.WaitAsync();
                            try { await _channel.BasicAckAsync(ea.DeliveryTag, false); }
                            finally { _channelLock.Release(); }
                        },
                        Nack = async (requeue) =>
                        {
                            await _channelLock.WaitAsync();
                            try { await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue); }
                            finally { _channelLock.Release(); }
                        }
                    };

                    await handler(context);
                    if (context.Ack != null)
                    {
                        await context.Ack();
                        context.Ack = null; // Prevent double-ack
                    }
                }
                catch (Exception ex)
                {
                    transaction.CaptureException(ex);
                    _logger.LogError(ex, "Error processing message from {Topic}/{Subscription}", topic, subscription);

                    // Handle retry with exponential backoff
                    await HandleRetryAsync(topic, subscription, ea, ex, options, cancellationToken);
                }
                finally
                {
                    transaction.End();
                }
            }
            finally
            {
                semaphore.Release();
            }
        };

        var consumerTag = await _channel.BasicConsumeAsync(subscription, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);

        return new RabbitMQSubscriptionHandler(topic, subscription, cts, _logger, _channel, consumerTag, semaphore, options.ConcurrentHandlers > 0 ? options.ConcurrentHandlers : 1);
    }

    private async Task HandleRetryAsync(string topic, string subscription, BasicDeliverEventArgs ea, Exception ex, SubscriptionOptions options, CancellationToken cancellationToken)
    {
        const string retryHeaderKey = "x-retry-count";
        var maxRetries = options.MaxRetries > 0 ? options.MaxRetries : _options.MaxRetries;
        var enableDlq = options.EnableDeadLetterQueue || _options.EnableDlq;

        // Get retry count from headers
        int retryCount = 0;
        if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.TryGetValue(retryHeaderKey, out var retryObj))
        {
            if (retryObj is int retryInt)
            {
                retryCount = retryInt;
            }
            else if (retryObj is byte[] retryBytes)
            {
                retryCount = BitConverter.ToInt32(retryBytes, 0);
            }
        }
        retryCount++;

        if (retryCount > maxRetries)
        {
            _logger.LogWarning("Max retries ({MaxRetries}) reached for message on topic {Topic}, subscription {Subscription}", maxRetries, topic, subscription);

            if (enableDlq)
            {
                // Send to DLQ
                await _channelLock.WaitAsync(cancellationToken);
                try { await _channel.BasicNackAsync(ea.DeliveryTag, false, false); }
                finally { _channelLock.Release(); }

                _logger.LogInformation("Message sent to DLQ: {Subscription}-dlq", subscription);
            }
            else
            {
                // Requeue if DLQ not enabled
                await _channelLock.WaitAsync(cancellationToken);
                try { await _channel.BasicNackAsync(ea.DeliveryTag, false, true); }
                finally { _channelLock.Release(); }
            }
            return;
        }

        // Calculate exponential backoff delay
        var delayMs = (int)Math.Pow(2, retryCount) * 1000; // 2s, 4s, 8s

        // Prepare headers with retry count and delay
        var headers = new Dictionary<string, object?>();
        if (ea.BasicProperties.Headers != null)
        {
            foreach (var header in ea.BasicProperties.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }
        headers[retryHeaderKey] = retryCount;
        headers["x-delay"] = delayMs;

        // Publish to retry exchange with delay
        var retryExchange = $"{topic}.retry";
        var retryRoutingKey = $"{subscription}.retry";

        var props = new global::RabbitMQ.Client.BasicProperties
        {
            Persistent = true,
            Headers = headers
        };

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            await _channel.BasicPublishAsync(
                exchange: retryExchange,
                routingKey: retryRoutingKey,
                mandatory: false,
                basicProperties: props,
                body: ea.Body,
                cancellationToken: cancellationToken);

            // Ack the original message since it's being retried
            await _channel.BasicAckAsync(ea.DeliveryTag, false);

            _logger.LogInformation("Message scheduled for retry {RetryCount}/{MaxRetries} with delay {DelayMs}ms on topic {Topic}",
                retryCount, maxRetries, delayMs, topic);
        }
        catch (Exception retryEx)
        {
            _logger.LogError(retryEx, "Failed to publish message to retry exchange {RetryExchange}. Nacking with requeue.", retryExchange);
            await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
        finally
        {
            _channelLock.Release();
        }
    }

}

internal class RabbitMQSubscriptionHandler : ISubscriptionHandler
{
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;
    private readonly global::RabbitMQ.Client.IChannel _channel;
    private readonly string _consumerTag;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;

    public string Topic { get; }
    public string Subscription { get; }

    public RabbitMQSubscriptionHandler(
        string topic,
        string subscription,
        CancellationTokenSource cts,
        ILogger logger,
        IChannel channel,
        string consumerTag,
        SemaphoreSlim semaphore,
        int maxConcurrency)
    {
        Topic = topic;
        Subscription = subscription;
        _cts = cts;
        _logger = logger;
        _channel = channel;
        _consumerTag = consumerTag;
        _semaphore = semaphore;
        _maxConcurrency = maxConcurrency;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping subscription: {Topic}/{Subscription}...", Topic, Subscription);

        try
        {
            await _channel.BasicCancelAsync(_consumerTag, noWait: false, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel consumer {ConsumerTag}", _consumerTag);
        }

        // Wait for all in-flight tasks to complete
        for (int i = 0; i < _maxConcurrency; i++)
        {
            await _semaphore.WaitAsync(cancellationToken);
        }

        await Task.Run(() => _cts.Cancel(), cancellationToken);
        _logger.LogInformation("Stopped subscription: {Topic}/{Subscription}", Topic, Subscription);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
