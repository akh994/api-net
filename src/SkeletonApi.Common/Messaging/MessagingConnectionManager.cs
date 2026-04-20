using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using global::RabbitMQ.Client;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;
using MQTTnet;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.RabbitMQ;

namespace SkeletonApi.Common.Messaging;

/// <summary>
/// Manages shared messaging resources (connections, pools, producers) across different broker types.
/// </summary>
public class MessagingConnectionManager : IDisposable
{
    private readonly ILogger<MessagingConnectionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    // RabbitMQ Pooling
    private readonly ConcurrentDictionary<string, RabbitMQConnectionPool> _rabbitPools = new();

    // Kafka Resource Caching
    private readonly ConcurrentDictionary<string, IProducer<string, byte[]>> _kafkaProducers = new();
    private readonly ConcurrentDictionary<string, IAdminClient> _kafkaAdmins = new();

    // PubSub Publisher Caching
    private readonly ConcurrentDictionary<string, PublisherClient> _pubsubPublishers = new();
    private readonly ConcurrentDictionary<string, PublisherServiceApiClient> _pubsubTopicAdmins = new();
    private readonly ConcurrentDictionary<string, SubscriberServiceApiClient> _pubsubSubAdmins = new();

    // MQTT Client Caching
    private readonly ConcurrentDictionary<string, IMqttClient> _mqttClients = new();

    public MessagingConnectionManager(ILogger<MessagingConnectionManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    #region RabbitMQ

    public IConnectionPool GetRabbitMQPool(RabbitMQOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var key = $"rabbitmq://{options.Username}@{options.Host}:{options.Port}/{options.VHost}";

        return _rabbitPools.GetOrAdd(key, (k) =>
        {
            _logger.LogInformation("Creating new RabbitMQ connection pool for {Key}", k);

            var factory = new global::RabbitMQ.Client.ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.Username,
                Password = options.Password,
                VirtualHost = options.VHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            var poolLogger = _loggerFactory.CreateLogger<RabbitMQConnectionPool>();
            return new RabbitMQConnectionPool(k, factory, 5, poolLogger);
        });
    }

    #endregion

    #region Kafka

    public IProducer<string, byte[]> GetKafkaProducer(KafkaOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var brokers = string.Join(",", options.Brokers.OrderBy(b => b));
        var key = $"kafka://{options.Username}@{brokers}/{options.ClientId}";

        return _kafkaProducers.GetOrAdd(key, (k) =>
        {
            _logger.LogInformation("Creating new Kafka producer for {Key}", k);

            var config = new ProducerConfig
            {
                BootstrapServers = brokers,
                ClientId = options.ClientId,
                Acks = Acks.All,
                EnableIdempotence = true,
                CompressionType = options.Compression?.ToLower() switch
                {
                    "gzip" => CompressionType.Gzip,
                    "snappy" => CompressionType.Snappy,
                    "lz4" => CompressionType.Lz4,
                    "zstd" => CompressionType.Zstd,
                    _ => CompressionType.None
                }
            };

            if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            {
                config.SaslMechanism = SaslMechanism.Plain;
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslUsername = options.Username;
                config.SaslPassword = options.Password;
            }

            return new ProducerBuilder<string, byte[]>(config).Build();
        });
    }

    public IAdminClient GetKafkaAdminClient(KafkaOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var brokers = string.Join(",", options.Brokers.OrderBy(b => b));
        var key = $"kafka-admin://{options.Username}@{brokers}";

        return _kafkaAdmins.GetOrAdd(key, (k) =>
        {
            _logger.LogInformation("Creating new Kafka Admin client for {Key}", k);

            var config = new AdminClientConfig
            {
                BootstrapServers = brokers,
            };

            if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            {
                config.SaslMechanism = SaslMechanism.Plain;
                config.SecurityProtocol = SecurityProtocol.SaslSsl;
                config.SaslUsername = options.Username;
                config.SaslPassword = options.Password;
            }

            return new AdminClientBuilder(config).Build();
        });
    }

    #endregion

    #region PubSub

    public async Task<PublisherClient> GetPubSubPublisherAsync(PubSubOptions options, string topic)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var key = $"pubsub://{options.ProjectId}/{topic}?creds={options.Credentials}";

        if (_pubsubPublishers.TryGetValue(key, out var client))
        {
            return client;
        }

        _logger.LogInformation("Creating new PubSub publisher for {Key}", key);

        var topicName = new TopicName(options.ProjectId, topic);
        PublisherClient newClient;

        if (!string.IsNullOrEmpty(options.Credentials))
        {
            if (!File.Exists(options.Credentials))
            {
                throw new FileNotFoundException($"PubSub credentials file not found at: {options.Credentials}. Please check your configuration.", options.Credentials);
            }

            using var stream = File.OpenRead(options.Credentials);
            var builder = new PublisherClientBuilder
            {
                TopicName = topicName,
                GoogleCredential = CredentialFactory.FromStream<GoogleCredential>(stream)
            };
            newClient = await builder.BuildAsync();
        }
        else
        {
            newClient = await PublisherClient.CreateAsync(topicName);
        }

        if (_pubsubPublishers.TryAdd(key, newClient))
        {
            return newClient;
        }

        await newClient.ShutdownAsync(TimeSpan.FromSeconds(5));
        return _pubsubPublishers[key];
    }

    public async Task<PublisherServiceApiClient> GetPubSubTopicAdminClientAsync(PubSubOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var key = $"pubsub-admin://{options.ProjectId}?creds={options.Credentials}";

        if (_pubsubTopicAdmins.TryGetValue(key, out var client))
        {
            return client;
        }

        _logger.LogInformation("Creating new PubSub Topic Admin client for {Key}", key);

        PublisherServiceApiClient newClient;
        if (!string.IsNullOrEmpty(options.Credentials))
        {
            if (!File.Exists(options.Credentials))
            {
                throw new FileNotFoundException($"PubSub credentials file not found at: {options.Credentials}", options.Credentials);
            }
            using var stream = File.OpenRead(options.Credentials);
            var builder = new PublisherServiceApiClientBuilder
            {
                GoogleCredential = CredentialFactory.FromStream<GoogleCredential>(stream)
            };
            newClient = await builder.BuildAsync();
        }
        else
        {
            newClient = await PublisherServiceApiClient.CreateAsync();
        }

        return _pubsubTopicAdmins.GetOrAdd(key, newClient);
    }

    public async Task<SubscriberServiceApiClient> GetPubSubSubscriptionAdminClientAsync(PubSubOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var key = $"pubsub-sub-admin://{options.ProjectId}?creds={options.Credentials}";

        if (_pubsubSubAdmins.TryGetValue(key, out var client))
        {
            return client;
        }

        _logger.LogInformation("Creating new PubSub Subscription Admin client for {Key}", key);

        SubscriberServiceApiClient newClient;
        if (!string.IsNullOrEmpty(options.Credentials))
        {
            if (!File.Exists(options.Credentials))
            {
                throw new FileNotFoundException($"PubSub credentials file not found at: {options.Credentials}", options.Credentials);
            }
            using var stream = File.OpenRead(options.Credentials);
            var builder = new SubscriberServiceApiClientBuilder
            {
                GoogleCredential = CredentialFactory.FromStream<GoogleCredential>(stream)
            };
            newClient = await builder.BuildAsync();
        }
        else
        {
            newClient = await SubscriberServiceApiClient.CreateAsync();
        }

        return _pubsubSubAdmins.GetOrAdd(key, newClient);
    }

    #endregion

    #region MQTT

    public async Task<IMqttClient> GetMqttClientAsync(MqttOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MessagingConnectionManager));

        var key = $"mqtt://{options.Username}@{options.Broker}/{options.ClientId}";

        if (_mqttClients.TryGetValue(key, out var client))
        {
            return client;
        }

        _logger.LogInformation("Creating new MQTT client for {Key}", key);

        var mqttFactory = new MqttClientFactory();
        var newClient = mqttFactory.CreateMqttClient();

        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithConnectionUri(options.Broker)
            .WithClientId(options.ClientId)
            .WithCleanSession(options.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(options.KeepAliveSeconds))
            .Build();

        if (!string.IsNullOrEmpty(options.Username) || !string.IsNullOrEmpty(options.Password))
        {
            mqttClientOptions = new MqttClientOptionsBuilder()
                .WithConnectionUri(options.Broker)
                .WithClientId(options.ClientId)
                .WithCredentials(options.Username, options.Password)
                .WithCleanSession(options.CleanSession)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(options.KeepAliveSeconds))
                .Build();
        }

        await newClient.ConnectAsync(mqttClientOptions);

        if (_mqttClients.TryAdd(key, newClient))
        {
            return newClient;
        }

        // Someone else added it while we were connecting
        await newClient.DisconnectAsync(new MqttClientDisconnectOptions());
        newClient.Dispose();
        return _mqttClients[key];
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing MessagingConnectionManager and all shared resources...");

        // Dispose RabbitMQ Pools
        foreach (var pool in _rabbitPools.Values)
        {
            try { pool.Dispose(); } catch (Exception ex) { _logger.LogError(ex, "Error disposing RabbitMQ pool"); }
        }
        _rabbitPools.Clear();

        // Dispose Kafka Producers
        foreach (var producer in _kafkaProducers.Values)
        {
            try
            {
                producer.Flush(TimeSpan.FromSeconds(5));
                producer.Dispose();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error disposing Kafka producer"); }
        }
        _kafkaProducers.Clear();

        // Dispose PubSub Publishers
        foreach (var publisher in _pubsubPublishers.Values)
        {
            try
            {
                publisher.ShutdownAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error disposing PubSub publisher"); }
        }
        _pubsubPublishers.Clear();

        // Dispose MQTT Clients
        foreach (var mqttClient in _mqttClients.Values)
        {
            try
            {
                mqttClient.DisconnectAsync(new MqttClientDisconnectOptions()).GetAwaiter().GetResult();
                mqttClient.Dispose();
            }
            catch (Exception ex) { _logger.LogError(ex, "Error disposing MQTT client"); }
        }
        _mqttClients.Clear();

        _disposed = true;
    }
}
