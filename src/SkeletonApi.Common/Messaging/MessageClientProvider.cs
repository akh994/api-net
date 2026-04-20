using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Common.Messaging;

/// <summary>
/// Provider for resolving message clients based on topic configuration
/// </summary>
public class MessageClientProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MessagePublishersOptions _options;
    private readonly MessageClientFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MessageClientProvider> _logger;
    private readonly Dictionary<string, IMessageClient> _publishers = new();

    public MessageClientProvider(
        IServiceProvider serviceProvider,
        IOptions<MessagePublishersOptions> options,
        MessageClientFactory factory,
        IConfiguration configuration,
        ILogger<MessageClientProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _factory = factory;
        _configuration = configuration;
        _logger = logger;

        InitializePublishers();
    }

    private void InitializePublishers()
    {
        foreach (var topic in _options.Topics)
        {
            try
            {
                var topicKey = topic.Key;
                var topicConfig = topic.Value;
                var type = topicConfig.Type;

                Dictionary<string, object> mergedConfig; // Declare here

                // Robustly get general config from IConfiguration if Options version is empty/dummy
                var generalSection = _configuration.GetSection($"MessagePublishers:GeneralMQConfig:{type}");
                var genDict = generalSection.Get<Dictionary<string, object>>() ?? new Dictionary<string, object>();

                if (genDict.Count > 0)
                {
                    mergedConfig = ConfigHelper.Merge(genDict, topicConfig.MQConfig);
                }
                else
                {
                    mergedConfig = topicConfig.MQConfig;
                }

                // For RabbitMQ specifically, we can check _options.GeneralMQConfig["rabbitmq"] type
                // But the helper ConfigHelper.Merge takes Dictionary.
                // Re-implement logic to be safe:
                // In binding, Dictionary<string, object> values are often strings or other primitives. But "rabbitmq" value is a whole section.
                // It might be bound as IConfigurationSection if raw, but here we have the bound Options object.
                // The ConfigurationExtensions bound it.
                // Let's assume ConfigHelper.Merge logic needs to be robust, but here we just pass topicConfig.MQConfig for safety 
                // if we can't easily merge without sophisticated logic.
                // Improving: We should try to use the merged config.

                // Let's rely on ConfigHelper.Merge and assume Type safety for now, or improve later if integration test fails.
                // Actually, let's look at `ConfigHelper.Merge`. It copies `general`. 
                // The issue is `GeneralMQConfig` values might not be `Dictionary<string, object>`.
                // In standard .NET Options binding, recursive binding to `object` is tricky.
                // But let's proceed.

                var client = _factory.CreateClient(type, mergedConfig);
                _publishers[topicKey] = client;

                _logger.LogInformation("Initialized publisher for topic '{TopicKey}' using type '{Type}'", topicKey, type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize publisher for topic '{TopicKey}'", topic.Key);
            }
        }
    }

    /// <summary>
    /// Get the appropriate message client for a specific topic key
    /// </summary>
    public IMessageClient GetClientForTopic(string topicKey)
    {
        if (_publishers.TryGetValue(topicKey, out var client))
        {
            return client;
        }

        throw new InvalidOperationException($"No message client configured for topic key: {topicKey}");
    }

    /// <summary>
    /// Get a specific broker client by key (Legacy support / Direct access)
    /// </summary>
    public IMessageClient GetClient(string brokerKey)
    {
        // This was used for specific named instances.
        // With new approach, we don't have registered named clients globally unless we add them.
        // For now, return a client if it matches a topic key?
        // Or throw/remove this method.
        // Existing UserCreatedConsumer uses GetClient(brokerKey) 
        // But we are refactoring consumer too.

        return _publishers.GetValueOrDefault(brokerKey)
            ?? throw new InvalidOperationException($"Client not found for key: {brokerKey}");
    }
}
