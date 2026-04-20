using Microsoft.Extensions.Options;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Examples;

/// <summary>
/// Example class showing how to use configuration options in your services
/// </summary>
public class ConfigurationUsageExample
{
    private readonly DatabaseOptions _databaseOptions;
    private readonly CacheOptions _cacheOptions;
    private readonly GrpcClientOptions _grpcClientOptions;
    private readonly RestClientOptions _restClientOptions;
    private readonly FeatureFlagOptions _featureFlagOptions;

    public ConfigurationUsageExample(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<CacheOptions> cacheOptions,
        IOptions<GrpcClientOptions> grpcClientOptions,
        IOptions<RestClientOptions> restClientOptions,
        IOptions<FeatureFlagOptions> featureFlagOptions)
    {
        _databaseOptions = databaseOptions.Value;
        _cacheOptions = cacheOptions.Value;
        _grpcClientOptions = grpcClientOptions.Value;
        _restClientOptions = restClientOptions.Value;
        _featureFlagOptions = featureFlagOptions.Value;
    }

    public void ExampleUsage()
    {
        // Database configuration
        var dbConnectionString = _databaseOptions.GetConnectionString();
        var maxConnections = _databaseOptions.MaxOpenConnections;

        // Cache configuration
        var redisConnectionString = _cacheOptions.GetConnectionString();
        var cacheDatabase = _cacheOptions.Database;

        // gRPC Client configuration
        var grpcAddress = _grpcClientOptions.UserService?.Address ?? "http://localhost:4022";
        var grpcFailureRatio = _grpcClientOptions.CircuitBreaker.FailureRatio;

        // REST Client configuration
        var restBaseUrl = _restClientOptions.UserService.BaseUrl;
        var restTimeout = _restClientOptions.UserService.TimeoutSeconds;
        var maxRetries = _restClientOptions.Retry.MaxAttempts;

        // Feature Flag configuration
        var featureFlagProvider = _featureFlagOptions.Provider;
        var featureFlagHost = _featureFlagOptions.Host;
    }
}
