using Microsoft.Extensions.Options;
using OpenFeature;
using OpenFeature.Contrib.Providers.Flipt;
using Serilog;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.FeatureFlags;

namespace SkeletonApi.Extensions;

public static class FeatureFlagExtensions
{
    /// <summary>
    /// Adds and initializes feature flag services with caching
    /// </summary>
    public static IServiceCollection AddFeatureFlagServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register base FeatureFlagService as singleton
        services.AddSingleton<FeatureFlagService>();

        // Register cache service
        services.AddMemoryCache(); // Required for IMemoryCache
        services.AddSingleton<FeatureFlagCacheService>();

        // Register cached wrapper service
        services.AddSingleton<CachedFeatureFlagService>();

        // Register background refresh service
        services.AddHostedService<FeatureFlagRefreshService>();

        // Initialize provider based on configuration
        var featureFlagOptions = configuration.GetSection("FeatureFlag").Get<FeatureFlagOptions>();

        if (featureFlagOptions != null)
        {
            InitializeProvider(featureFlagOptions);
        }

        return services;
    }

    private static void InitializeProvider(FeatureFlagOptions options)
    {
        switch (options.Provider?.ToLowerInvariant())
        {
            case "flipt":
                Log.Information("Initializing Flipt feature flag provider at {Host} with timeout {TimeoutSeconds}s",
                    options.Host, options.TimeoutSeconds);
                var fliptProvider = new FliptProvider(
                    options.Host,
                    options.NamespaceKey,
                    options.ClientToken,
                    options.TimeoutSeconds);
                Api.Instance.SetProviderAsync(fliptProvider).Wait();
                break;

            case "go-feature-flag":
                Log.Warning("Go Feature Flag provider not yet implemented for .NET, using NoOp provider");
                // Go Feature Flag provider would be initialized here if available
                break;

            default:
                Log.Warning("No valid feature flag provider configured (Provider: {Provider}), using NoOp provider", options.Provider);
                break;
        }

        // Log cache configuration
        if (options.Cache.Enabled)
        {
            Log.Information("Feature flag caching enabled: TTL={TtlSeconds}s, Refresh={RefreshSeconds}s, Warmup={WarmupCount} flags",
                options.Cache.TtlSeconds, options.Cache.RefreshSeconds, options.Cache.WarmupFlags.Count);
        }
        else
        {
            Log.Information("Feature flag caching disabled");
        }
    }
}

