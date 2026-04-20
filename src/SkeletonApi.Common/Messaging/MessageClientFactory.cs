using System.Text.Json;
using Confluent.Kafka;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;
using SkeletonApi.Common.Messaging.RabbitMQ;

namespace SkeletonApi.Common.Messaging;

public class MessageClientFactory
{
    private readonly MessagingConnectionManager _connectionManager;
    private readonly ILoggerFactory _loggerFactory;

    public MessageClientFactory(
        MessagingConnectionManager connectionManager,
        ILoggerFactory loggerFactory)
    {
        _connectionManager = connectionManager;
        _loggerFactory = loggerFactory;
    }

    public IMessageClient CreateClient(string type, Dictionary<string, object> configDict)
    {
        // Convert dictionary to JsonElement for easy deserialization
        var json = JsonSerializer.Serialize(configDict);
        var config = JsonSerializer.Deserialize<JsonElement>(json);

        return type.ToLower() switch
        {
            "rabbitmq" => CreateRabbitMQClient(config),
            "kafka" => CreateKafkaClient(config),
            "pubsub" => CreatePubSubClient(config),
            "mqtt" => CreateMqttClient(config),
            _ => throw new NotSupportedException($"Broker type '{type}' is not supported")
        };
    }

    private IMessageClient CreateRabbitMQClient(JsonElement config)
    {
        var serializeOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };
        serializeOptions.Converters.Add(new SkeletonApi.Common.Configuration.BooleanConverter());
        var options = config.Deserialize<RabbitMQOptions>(serializeOptions) ?? new RabbitMQOptions();
        var pool = _connectionManager.GetRabbitMQPool(options);
        var logger = _loggerFactory.CreateLogger<RabbitMQ.RabbitMQClient>();
        return new RabbitMQ.RabbitMQClient(options, logger, pool);
    }

    private IMessageClient CreateKafkaClient(JsonElement config)
    {
        var options = config.Deserialize<KafkaOptions>() ?? new KafkaOptions();
        var producer = _connectionManager.GetKafkaProducer(options);
        var logger = _loggerFactory.CreateLogger<Kafka.KafkaClient>();
        return new Kafka.KafkaClient(options, logger, producer, _connectionManager);
    }

    private IMessageClient CreatePubSubClient(JsonElement config)
    {
        var options = config.Deserialize<PubSubOptions>() ?? new PubSubOptions();
        var logger = _loggerFactory.CreateLogger<PubSub.PubSubClient>();
        return new PubSub.PubSubClient(options, logger, _connectionManager);
    }

    private IMessageClient CreateMqttClient(JsonElement config)
    {
        var options = config.Deserialize<MqttOptions>() ?? new MqttOptions();
        var client = _connectionManager.GetMqttClientAsync(options).GetAwaiter().GetResult();
        var logger = _loggerFactory.CreateLogger<MqttClient>();
        return new MqttClient(options, logger, client);
    }
}
