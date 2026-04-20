using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Errors;
using SkeletonApi.Common.Interfaces;
using SkeletonApi.Common.Messaging.Abstractions;
using SkeletonApi.Common.Models;

namespace SkeletonApi.Common.Messaging.Abstractions;

/// <summary>
/// Base class for message consumers that run as background services
/// </summary>
public abstract class BaseMessageConsumer<TMessage> : BackgroundService
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly ILogger Logger;
    protected readonly MessageConsumersOptions Options;
    protected readonly MessageClientFactory ClientFactory;
    protected readonly IConfiguration Configuration;
    protected readonly List<ISubscriptionHandler> SubscriptionHandlers = new();

    protected abstract string Topic { get; }

    protected BaseMessageConsumer(
        IServiceProvider serviceProvider,
        IOptions<MessageConsumersOptions> options,
        MessageClientFactory clientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        IConfiguration configuration)
    {
        ServiceProvider = serviceProvider;
        Options = options.Value;
        ClientFactory = clientFactory;
        ScopeFactory = scopeFactory;
        Logger = logger;
        Configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting consumer for topic {Topic}", Topic);

        try
        {
            bool subscribed = false;

            // Iterate all configured topics to find matches for our Topic key
            foreach (var topicEntry in Options.Topics)
            {
                var key = topicEntry.Key;
                var config = topicEntry.Value;

                // Match if Key is Topic OR configured Name is Topic
                if (key.Equals(Topic, StringComparison.OrdinalIgnoreCase) ||
                    config.Name.Equals(Topic, StringComparison.OrdinalIgnoreCase))
                {
                    await SubscribeToTopicAsync(key, config, stoppingToken);
                    subscribed = true;
                }
            }

            if (!subscribed)
            {
                Logger.LogWarning("No configuration found for topic '{Topic}', Consumer will be idle.", Topic);
            }

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Fatal error in consumer for topic {Topic}", Topic);
            throw;
        }
    }

    private async Task SubscribeToTopicAsync(string key, ConsumerTopicConfig config, CancellationToken stoppingToken)
    {
        try
        {
            // Merge generic config with specific config
            Dictionary<string, object> mergedConfig = new Dictionary<string, object>();

            // Robustly get general config from IConfiguration if OOptions version is empty/dummy
            var generalSection = Configuration.GetSection($"MessageConsumers:GeneralMQConfig:{config.Type}");
            var genDict = generalSection.Get<Dictionary<string, object>>() ?? new Dictionary<string, object>();

            mergedConfig = ConfigHelper.Merge(genDict, config.MQConfig);

            var client = ClientFactory.CreateClient(config.Type, mergedConfig);

            // Determine subscription name
            var subscriptionName = !string.IsNullOrEmpty(Options.Subscription) ? Options.Subscription : "skeleton-api-net";

            var subOptions = new SubscriptionOptions
            {
                ConcurrentHandlers = 1,
                EnableDeadLetterQueue = true,
                PrefetchCount = 10,
                MessageTtlMs = 7 * 24 * 60 * 60 * 1000 // 7 days
            };

            // Enhanced mapping from config (supports PascalCase and snake_case)
            if (mergedConfig.TryGetValue("ConcurrentConsumers", out var ccObj) || mergedConfig.TryGetValue("concurrent_consumers", out ccObj))
            {
                if (ccObj is int cc) subOptions.ConcurrentHandlers = cc;
                else if (ccObj != null && int.TryParse(ccObj.ToString(), out int ccParsed)) subOptions.ConcurrentHandlers = ccParsed;
            }

            if (mergedConfig.TryGetValue("MaxRetries", out var mrObj) || mergedConfig.TryGetValue("max_retries", out mrObj))
            {
                if (mrObj is int mr) subOptions.MaxRetries = mr;
                else if (mrObj != null && int.TryParse(mrObj.ToString(), out int mrParsed)) subOptions.MaxRetries = mrParsed;
            }

            if (mergedConfig.TryGetValue("EnableDlq", out var edObj) || mergedConfig.TryGetValue("enable_dlq", out edObj))
            {
                if (edObj is bool ed) subOptions.EnableDeadLetterQueue = ed;
                else if (edObj != null && bool.TryParse(edObj.ToString(), out bool edParsed)) subOptions.EnableDeadLetterQueue = edParsed;
            }

            if (mergedConfig.TryGetValue("MessageTtl", out var mtObj) || mergedConfig.TryGetValue("message_ttl", out mtObj))
            {
                if (mtObj is int mt) subOptions.MessageTtlMs = mt * 1000;
                else if (mtObj != null && int.TryParse(mtObj.ToString(), out int mtParsed)) subOptions.MessageTtlMs = mtParsed * 1000;
            }

            if (mergedConfig.TryGetValue("PrefetchCount", out var pcObj) || mergedConfig.TryGetValue("prefetch_count", out pcObj))
            {
                if (pcObj is ushort pc) subOptions.PrefetchCount = pc;
                else if (pcObj != null && ushort.TryParse(pcObj.ToString(), out ushort pcParsed)) subOptions.PrefetchCount = pcParsed;
            }

            var handler = await client.SubscribeAsync(
                config.Name,
                $"{subscriptionName}-{key}",
                async context => await InternalHandleMessageAsync(context, stoppingToken),
                subOptions,
                stoppingToken);

            SubscriptionHandlers.Add(handler);
            Logger.LogInformation(
                "Subscribed to topic '{Topic}' using {Type} (Key: {Key})",
                config.Name,
                config.Type,
                key);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to topic key {Key}", key);
        }
    }

    private async Task InternalHandleMessageAsync(MessageContext context, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = ScopeFactory.CreateScope();

            // Extract claims and populate context
            if (context.Headers != null)
            {
                var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();
                ExtractClaims(context.Headers, userContext);
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var message = JsonSerializer.Deserialize<TMessage>(context.Body, options);
            if (message != null)
            {
                await ProcessMessageAsync(message, scope, stoppingToken);
            }
        }
        catch (Exception ex) when (!IsRetryable(ex))
        {
            // Terminal error: log and ACK (do not re-throw) to avoid infinite retries
            Logger.LogError(ex, "Terminal error processing message from {Topic} - message will be ACKed", Topic);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing message from {Topic} - message will be NACKed for retry", Topic);
            throw;
        }
    }

    /// <summary>
    /// Returns false for terminal (non-retryable) errors that should be ACKed immediately.
    /// This mirrors the IsRetryable logic in skeleton-api-go's pkg/errors.
    /// </summary>
    private static bool IsRetryable(Exception ex)
    {
        // Terminal custom domain exceptions - no point retrying
        if (ex is NotFoundException
            || ex is ValidationException
            || ex is BadRequestException
            || ex is ConflictException
            || ex is UnauthorizedException
            || ex is ForbiddenException
            || ex is DataAccessHubException)
        {
            return false;
        }

        // Terminal gRPC status codes
        if (ex is RpcException rpcEx)
        {
            return rpcEx.StatusCode != StatusCode.NotFound
                && rpcEx.StatusCode != StatusCode.InvalidArgument
                && rpcEx.StatusCode != StatusCode.AlreadyExists
                && rpcEx.StatusCode != StatusCode.PermissionDenied
                && rpcEx.StatusCode != StatusCode.Unauthenticated
                && rpcEx.StatusCode != StatusCode.FailedPrecondition
                && rpcEx.StatusCode != StatusCode.OutOfRange
                && rpcEx.StatusCode != StatusCode.Unimplemented;
        }

        return true;
    }

    protected abstract Task ProcessMessageAsync(TMessage message, IServiceScope scope, CancellationToken cancellationToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var handler in SubscriptionHandlers)
        {
            await handler.StopAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }

    private void ExtractClaims(IDictionary<string, string> headers, IUserContext userContext)
    {
        var claims = new UserClaims();
        bool hasClaims = false;

        if (headers.TryGetValue("x-user-id", out var userId))
        {
            claims.UserId = userId;
            hasClaims = true;
        }

        if (headers.TryGetValue("x-user-uuid", out var userUuidStr) && Guid.TryParse(userUuidStr, out var userUuid))
        {
            claims.UserUuid = userUuid;
            hasClaims = true;
        }

        if (headers.TryGetValue("x-domain-id", out var domainIdStr) && Guid.TryParse(domainIdStr, out var domainId))
        {
            claims.DomainId = domainId;
            hasClaims = true;
        }

        if (headers.TryGetValue("x-domain-name", out var domainName))
        {
            claims.DomainName = domainName;
        }

        if (headers.TryGetValue("x-domain-type", out var domainType))
        {
            claims.DomainType = domainType;
        }

        if (headers.TryGetValue("x-group-name", out var groupName))
        {
            claims.GroupName = groupName;
        }

        if (headers.TryGetValue("x-user-email", out var email))
        {
            claims.Email = email;
        }

        if (headers.TryGetValue("x-phone-number", out var phoneNumber))
        {
            claims.PhoneNumber = phoneNumber;
        }

        if (headers.TryGetValue("x-user-role", out var role))
        {
            claims.Role = role;
        }

        // Extract extra attributes from JSON header
        if (headers.TryGetValue("x-extra-attributes", out var extraAttributesJson))
        {
            claims.ExtraAttributes = UserClaims.DeserializeExtraAttributes(extraAttributesJson);
        }

        if (hasClaims)
        {
            userContext.SetUser(claims);
            Logger.LogDebug("Consumer: Extracted claims from message. UserId: {UserId}, Role: {Role}", claims.UserId, claims.Role);
        }
    }
}
