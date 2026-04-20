using Microsoft.Extensions.DependencyInjection;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Extensions;

public static class ConfigurationExtensions
{
    public static IServiceCollection AddConfigurationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind all configuration sections
        services.Configure<ServerOptions>(configuration.GetSection("Server"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<CacheOptions>(configuration.GetSection("Cache"));
        services.Configure<MessagePublishersOptions>(configuration.GetSection("MessagePublishers"));
        services.Configure<MessageConsumersOptions>(configuration.GetSection("MessageConsumers"));
        services.Configure<HttpClientOptions>(configuration.GetSection("HttpClient"));
        services.Configure<GrpcClientOptions>(configuration.GetSection("GrpcClient"));
        services.Configure<RestClientOptions>(configuration.GetSection("RestClient"));
        services.Configure<FeatureFlagOptions>(configuration.GetSection("FeatureFlag"));
        services.Configure<ProfilingOptions>(configuration.GetSection("Profiling"));
        services.Configure<ObservabilityOptions>(configuration.GetSection("Observability"));

        return services;
    }
}
