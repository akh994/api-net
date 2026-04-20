using FluentValidation;
using HealthChecks.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Application.Services;
using SkeletonApi.Application.Validators;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Extensions;
using SkeletonApi.Common.Messaging;
using SkeletonApi.Common.Messaging.Abstractions;
using SkeletonApi.Common.Messaging.RabbitMQ;
using SkeletonApi.Domain.Entities;
using SkeletonApi.Infrastructure.Clients;
using SkeletonApi.Infrastructure.Interfaces;
using SkeletonApi.Infrastructure.Repositories;
using SkeletonApi.Infrastructure.SSE;
using StackExchange.Redis;

namespace SkeletonApi.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Register Common Services
        services.AddCommonServices();

        // Register Redis Master Connection
        services.AddKeyedSingleton<StackExchange.Redis.IConnectionMultiplexer>("RedisMaster", (sp, key) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var cacheOptions = configuration.GetSection("Cache").Get<SkeletonApi.Common.Configuration.CacheOptions>()
                ?? new SkeletonApi.Common.Configuration.CacheOptions();

            var configOptions = ConfigurationOptions.Parse(cacheOptions.GetConnectionString());
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 3;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;

            return StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
        });

        // Register Redis Replica Connection (if configured)
        services.AddKeyedSingleton<StackExchange.Redis.IConnectionMultiplexer>("RedisReplica", (sp, key) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var cacheOptions = configuration.GetSection("Cache").Get<SkeletonApi.Common.Configuration.CacheOptions>()
                ?? new SkeletonApi.Common.Configuration.CacheOptions();

            // If replica is configured, use it; otherwise fallback to master
            if (cacheOptions.Replica != null)
            {
                var configOptions = ConfigurationOptions.Parse(cacheOptions.Replica.GetConnectionString());
                configOptions.AbortOnConnectFail = false;
                configOptions.ConnectRetry = 3;
                configOptions.ConnectTimeout = 5000;
                configOptions.SyncTimeout = 5000;

                return StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
            }
            else
            {
                // Fallback to master if no replica configured
                return sp.GetRequiredKeyedService<StackExchange.Redis.IConnectionMultiplexer>("RedisMaster");
            }
        });

        // Register default IConnectionMultiplexer as master for backward compatibility
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
            sp.GetRequiredKeyedService<StackExchange.Redis.IConnectionMultiplexer>("RedisMaster"));


        // Register Repositories
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<ICacheRepository, RedisCacheRepository>();
        services.AddSingleton<ISseManager, SseManager>();
        services.AddSingleton<SkeletonApi.Common.Concurrency.ISingleFlight, SkeletonApi.Common.Concurrency.SingleFlight>();

        // Register gRPC Client
        services.AddGrpcClient<SkeletonApi.Protos.UserGrpcService.UserGrpcServiceClient>((sp, o) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var grpcOptions = configuration.GetSection("GrpcClient").Get<GrpcClientOptions>() ?? new GrpcClientOptions();
            o.Address = new Uri(grpcOptions.UserService?.Address ?? "http://localhost:4022");
        });

        // Register GrpcClientRepository for UserGrpcService
        services.AddScoped<SkeletonApi.Common.GrpcClient.GrpcClientRepository<SkeletonApi.Protos.UserGrpcService.UserGrpcServiceClient>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<SkeletonApi.Common.GrpcClient.GrpcClientRepository<SkeletonApi.Protos.UserGrpcService.UserGrpcServiceClient>>>();
            var options = configuration.GetSection("GrpcClient").Get<GrpcClientOptions>() ?? new GrpcClientOptions();
            var interceptor = sp.GetRequiredService<SkeletonApi.Common.GrpcClient.ClaimsPropagationInterceptor>();

            var address = options.UserService?.Address ?? "http://localhost:4022";

            return new SkeletonApi.Common.GrpcClient.GrpcClientRepository<SkeletonApi.Protos.UserGrpcService.UserGrpcServiceClient>(
                address,
                options.GetCircuitBreakerForUserService(),
                options.GetTlsForUserService(),
                logger,
                invoker => new SkeletonApi.Protos.UserGrpcService.UserGrpcServiceClient(invoker),
                interceptor
            );
        });

        // Register REST Client
        services.AddHttpClient("SkeletonApiClient", (sp, client) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var restOptions = configuration.GetSection("RestClient").Get<RestClientOptions>() ?? new RestClientOptions();
            client.BaseAddress = new Uri(restOptions.UserService.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(restOptions.UserService.TimeoutSeconds);
        })
        .AddHttpMessageHandler<SkeletonApi.Common.RestClient.ClaimsPropagationHandler>();

        // Register RestClientRepository
        services.AddScoped<SkeletonApi.Common.RestClient.RestClientRepository>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<SkeletonApi.Common.RestClient.RestClientRepository>>();
            var httpClient = httpClientFactory.CreateClient("SkeletonApiClient");
            var options = configuration.GetSection("RestClient").Get<RestClientOptions>() ?? new RestClientOptions();

            return new SkeletonApi.Common.RestClient.RestClientRepository(options, logger, httpClient);
        });

        // Register Client Repositories
        services.AddScoped<IUserGrpcRepository, UserGrpcRepository>();
        services.AddScoped<IUserRestRepository, UserRestRepository>();

        // Register Messaging Services
        services.AddSingleton<MessagingConnectionManager>();
        services.AddSingleton<MessageClientFactory>();

        // Register MessageClientProvider
        services.AddSingleton<MessageClientProvider>();

        services.AddScoped<IUserMessagePublisher, SkeletonApi.Infrastructure.Messaging.UserMessagePublisher>();

        // Register Consumers
        services.AddHostedService<SkeletonApi.Consumers.UserCreatedConsumer>();
        services.AddHostedService<SseSubscriptionService>();

        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register Services
        services.AddScoped<IUserService, UserService>();

        // Register Validators
        services.AddScoped<IValidator<User>, UserValidator>();

        return services;
    }

    public static IServiceCollection AddHealthChecksConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseOptions = configuration.GetSection("Database").Get<SkeletonApi.Common.Configuration.DatabaseOptions>()
            ?? new SkeletonApi.Common.Configuration.DatabaseOptions();
        var cacheOptions = configuration.GetSection("Cache").Get<SkeletonApi.Common.Configuration.CacheOptions>()
            ?? new SkeletonApi.Common.Configuration.CacheOptions();
        var rabbitMQOptionsSection = configuration.GetSection("MessageConsumers:GeneralMQConfig:rabbitmq");
        var rabbitMQOptions = rabbitMQOptionsSection.Get<SkeletonApi.Common.Configuration.RabbitMQOptions>()
            ?? new SkeletonApi.Common.Configuration.RabbitMQOptions();

        var healthChecks = services.AddHealthChecks();

        switch (databaseOptions.Provider.ToLower())
        {
            case "postgresql":
                healthChecks.AddNpgSql(
                    connectionString: databaseOptions.GetConnectionString(),
                    name: "postgresql",
                    tags: new[] { "db", "ready" });
                break;
            case "sqlserver":
                healthChecks.AddSqlServer(
                    connectionString: databaseOptions.GetConnectionString(),
                    name: "sqlserver",
                    tags: new[] { "db", "ready" });
                break;
            default:
                healthChecks.AddMySql(
                    connectionString: databaseOptions.GetConnectionString(),
                    name: "mysql",
                    tags: new[] { "db", "ready" });
                break;
        }

        // Register a singleton RabbitMQ connection for healthcheck (reused across all checks)
        services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
        {
            var factory = new RabbitMQ.Client.ConnectionFactory()
            {
                Uri = new Uri(rabbitMQOptions.GetConnectionString())
            };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        healthChecks
            .AddRedis(
                redisConnectionString: cacheOptions.GetConnectionString(),
                name: "redis",
                tags: new[] { "cache", "ready" })
            .AddRabbitMQ(
                name: "rabbitmq",
                tags: new[] { "messaging", "ready" });

        return services;
    }
}
