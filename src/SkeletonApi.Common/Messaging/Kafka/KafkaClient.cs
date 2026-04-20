using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Common.Messaging.Kafka;

public class KafkaClient : IMessageClient
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly ILogger<KafkaClient> _logger;
    private readonly KafkaOptions _options;
    private readonly MessagingConnectionManager _connectionManager;
    private readonly List<KafkaSubscriptionHandler> _handlers = new();
    private readonly ConcurrentDictionary<string, bool> _ensuredTopics = new();
    private bool _disposed;

    public KafkaClient(KafkaOptions options, ILogger<KafkaClient> logger, IProducer<string, byte[]> producer, MessagingConnectionManager connectionManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

        _logger.LogInformation("Kafka client initialized with shared producer for brokers: {Brokers}", string.Join(",", options.Brokers));
    }

    public async Task PublishAsync(string topic, byte[] message, CancellationToken cancellationToken = default)
    {
        await PublishAsync(topic, message, new Dictionary<string, string>(), cancellationToken);
    }

    public async Task PublishAsync(string topic, byte[] message, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));

        if (_options.CreateTopics)
        {
            await EnsureTopicAsync(topic, cancellationToken);
        }

        var kafkaMessage = new Message<string, byte[]>
        {
            Key = Guid.NewGuid().ToString(),
            Value = message,
            Headers = new Headers()
        };

        foreach (var header in headers)
        {
            kafkaMessage.Headers.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
        }

        var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
        _logger.LogDebug("Published message to Kafka topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
            topic, result.Partition.Value, result.Offset.Value);
    }

    private async Task EnsureTopicAsync(string topic, CancellationToken ct)
    {
        if (_ensuredTopics.TryGetValue(topic, out _)) return;

        try
        {
            var admin = _connectionManager.GetKafkaAdminClient(_options);
            var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(5));

            if (metadata.Topics.Count == 0 || metadata.Topics[0].Error.IsError)
            {
                _logger.LogInformation("Kafka Topic {Topic} not found, creating...", topic);
                await admin.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }
                });
            }

            _ensuredTopics.TryAdd(topic, true);
        }
        catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _ensuredTopics.TryAdd(topic, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Kafka topic {Topic} exists. Infrastructure might already exist or permissions are missing.", topic);
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
        if (_options.CreateTopics)
        {
            await EnsureTopicAsync(topic, cancellationToken);
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = string.Join(",", _options.Brokers),
            GroupId = _options.GroupId,
            ClientId = _options.ClientId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        // Add SASL authentication if username/password provided
        if (!string.IsNullOrEmpty(_options.Username) && !string.IsNullOrEmpty(_options.Password))
        {
            consumerConfig.SaslMechanism = SaslMechanism.Plain;
            consumerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            consumerConfig.SaslUsername = _options.Username;
            consumerConfig.SaslPassword = _options.Password;
        }

        var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe(topic);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var consumerTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(cts.Token);

                        var msgHeaders = new Dictionary<string, string>();
                        if (consumeResult.Message.Headers != null)
                        {
                            foreach (var header in consumeResult.Message.Headers)
                            {
                                msgHeaders[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
                            }
                        }

                        var context = new MessageContext
                        {
                            Topic = topic,
                            Subscription = subscription,
                            Body = consumeResult.Message.Value,
                            Headers = msgHeaders,
                            MessageId = consumeResult.Message.Key ?? Guid.NewGuid().ToString(),
                            Timestamp = consumeResult.Message.Timestamp.UtcDateTime
                        };

                        context.Ack = async () =>
                        {
                            await Task.Run(() => consumer.Commit(consumeResult));
                            context.Ack = null;
                        };

                        context.Nack = async (requeue) =>
                        {
                            await Task.Run(() =>
                            {
                                // Kafka doesn't have native NACK, we just don't commit
                                // For DLQ, you would publish to a separate topic
                                if (!requeue && options.EnableDeadLetterQueue)
                                {
                                    var dlqTopic = $"{topic}-dlq";
                                    _producer.Produce(dlqTopic, consumeResult.Message);
                                }

                                // FIX: Offset MUST be committed locally even after Nack/DLQ to prevent infinite redelivery
                                consumer.Commit(consumeResult);
                            });
                            context.Ack = null;
                        };

                        try
                        {
                            await handler(context);
                            if (context.Ack != null)
                            {
                                await context.Ack();
                            }
                        }
                        catch (Exception innerEx)
                        {
                            _logger.LogError(innerEx, "Error processing Kafka message from {Topic}/{Subscription}", topic, subscription);
                            if (context.Nack != null)
                            {
                                await context.Nack(false); // Route to DLQ on Exception
                            }
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Error consuming from Kafka topic: {Topic}", topic);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Kafka message from {Topic}/{Subscription}", topic, subscription);
                    }
                }
            }
            finally
            {
                consumer.Close();
            }
        }, cts.Token);

        var subscriptionHandler = new KafkaSubscriptionHandler(topic, subscription, consumer, cts, consumerTask, _logger);
        _handlers.Add(subscriptionHandler);

        return await Task.FromResult(subscriptionHandler);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var handler in _handlers)
        {
            handler?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            handler?.Dispose();
        }
        _handlers.Clear();

        // Shared producer is NOT disposed here; it's managed by MessagingConnectionManager.

        _disposed = true;
        _logger.LogInformation("Kafka client disposed");
    }
}

internal class KafkaSubscriptionHandler : ISubscriptionHandler
{
    private readonly IConsumer<string, byte[]> _consumer;
    private readonly CancellationTokenSource _cts;
    private readonly Task _consumerTask;
    private readonly ILogger _logger;

    public string Topic { get; }
    public string Subscription { get; }

    public KafkaSubscriptionHandler(
        string topic,
        string subscription,
        IConsumer<string, byte[]> consumer,
        CancellationTokenSource cts,
        Task consumerTask,
        ILogger logger)
    {
        Topic = topic;
        Subscription = subscription;
        _consumer = consumer;
        _cts = cts;
        _consumerTask = consumerTask;
        _logger = logger;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Kafka subscription: {Topic}/{Subscription}...", Topic, Subscription);
        await Task.Run(() => _cts.Cancel(), cancellationToken);

        try
        {
            // Wait for the background loop to complete
            await _consumerTask;
        }
        catch (OperationCanceledException) { /* Expected */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for Kafka consumer task to stop");
        }

        _logger.LogInformation("Stopped Kafka subscription: {Topic}/{Subscription}", Topic, Subscription);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
