using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.FeatureFlags;

/// <summary>
/// Background service for periodic feature flag cache refresh
/// </summary>
public class FeatureFlagRefreshService : BackgroundService
{
    private readonly CachedFeatureFlagService _cachedService;
    private readonly FeatureFlagCacheOptions _cacheOptions;
    private readonly ILogger<FeatureFlagRefreshService> _logger;
    private readonly TimeSpan _refreshInterval;

    public FeatureFlagRefreshService(
        CachedFeatureFlagService cachedService,
        IOptions<FeatureFlagOptions> options,
        ILogger<FeatureFlagRefreshService> logger)
    {
        _cachedService = cachedService;
        _cacheOptions = options.Value.Cache;
        _logger = logger;
        _refreshInterval = TimeSpan.FromSeconds(_cacheOptions.RefreshSeconds > 0 ? _cacheOptions.RefreshSeconds : 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cacheOptions.Enabled)
        {
            _logger.LogInformation("Feature flag cache is disabled, refresh service will not run");
            return;
        }

        _logger.LogInformation("Starting feature flag cache refresh service with interval: {Interval}", _refreshInterval);

        // Warmup cache on startup
        try
        {
            await _cachedService.WarmupCacheAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warmup feature flag cache");
        }

        // Periodic refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RefreshCacheAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during feature flag cache refresh");
            }
        }

        _logger.LogInformation("Feature flag cache refresh service stopped");
    }

    private async Task RefreshCacheAsync()
    {
        _logger.LogDebug("Refreshing feature flag cache");

        // Refresh warmup flags
        if (_cacheOptions.WarmupFlags.Count > 0)
        {
            foreach (var flag in _cacheOptions.WarmupFlags)
            {
                try
                {
                    // Force refresh by invalidating first
                    _cachedService.InvalidateFlag(flag);
                    await _cachedService.IsEnabledAsync(flag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh flag: {FlagName}", flag);
                }
            }
        }

        _logger.LogDebug("Feature flag cache refresh completed");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping feature flag cache refresh service");
        await base.StopAsync(cancellationToken);
    }
}
