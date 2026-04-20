using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFeature.Model;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.FeatureFlags;

/// <summary>
/// Cached feature flag service wrapper
/// </summary>
public class CachedFeatureFlagService
{
    private readonly FeatureFlagService _innerService;
    private readonly FeatureFlagCacheService _cacheService;
    private readonly FeatureFlagCacheOptions _cacheOptions;
    private readonly ILogger<CachedFeatureFlagService> _logger;

    public CachedFeatureFlagService(
        FeatureFlagService innerService,
        FeatureFlagCacheService cacheService,
        IOptions<FeatureFlagOptions> options,
        ILogger<CachedFeatureFlagService> logger)
    {
        _innerService = innerService;
        _cacheService = cacheService;
        _cacheOptions = options.Value.Cache;
        _logger = logger;
    }

    /// <summary>
    /// Check if a feature is enabled (with caching)
    /// </summary>
    public async Task<bool> IsEnabledAsync(string featureName, bool defaultValue = false, EvaluationContext? context = null)
    {
        // Try cache first
        var cached = _cacheService.Get(featureName);
        if (cached != null && cached.FlagType == "boolean")
        {
            _logger.LogDebug("Feature flag cache hit: {FlagName} = {Value}", featureName, cached.Value);
            return (bool)(cached.Value ?? defaultValue);
        }

        // Cache miss - fetch from provider
        _logger.LogDebug("Feature flag cache miss: {FlagName}", featureName);

        bool value = defaultValue;
        Exception? error = null;
        bool fromFlipt = false;

        try
        {
            value = await _innerService.IsEnabledAsync(featureName, defaultValue, context);
            fromFlipt = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feature flag from provider, using default: {FlagName} = {DefaultValue}",
                featureName, defaultValue);
            error = ex;
            _cacheService.RecordError();
        }

        // Store in cache
        var entry = new FeatureFlagCacheEntry
        {
            Value = value,
            FlagType = "boolean",
            FromFlipt = fromFlipt,
            Error = error
        };

        _cacheService.Set(featureName, entry);

        return value;
    }

    /// <summary>
    /// Get string feature value (with caching)
    /// </summary>
    public async Task<string> GetStringValueAsync(string featureName, string defaultValue, EvaluationContext? context = null)
    {
        // Try cache first
        var cached = _cacheService.Get(featureName);
        if (cached != null && cached.FlagType == "string")
        {
            _logger.LogDebug("Feature flag cache hit: {FlagName} = {Value}", featureName, cached.Value);
            return (string)(cached.Value ?? defaultValue);
        }

        // Cache miss - fetch from provider
        _logger.LogDebug("Feature flag cache miss: {FlagName}", featureName);

        string value = defaultValue;
        Exception? error = null;
        bool fromFlipt = false;

        try
        {
            value = await _innerService.GetStringValueAsync(featureName, defaultValue, context);
            fromFlipt = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feature flag from provider, using default: {FlagName} = {DefaultValue}",
                featureName, defaultValue);
            error = ex;
            _cacheService.RecordError();
        }

        // Store in cache
        var entry = new FeatureFlagCacheEntry
        {
            Value = value,
            FlagType = "string",
            FromFlipt = fromFlipt,
            Error = error
        };

        _cacheService.Set(featureName, entry);

        return value;
    }

    /// <summary>
    /// Get integer feature value (with caching)
    /// </summary>
    public async Task<int> GetIntValueAsync(string featureName, int defaultValue, EvaluationContext? context = null)
    {
        // Try cache first
        var cached = _cacheService.Get(featureName);
        if (cached != null && cached.FlagType == "int")
        {
            _logger.LogDebug("Feature flag cache hit: {FlagName} = {Value}", featureName, cached.Value);
            return (int)(cached.Value ?? defaultValue);
        }

        // Cache miss - fetch from provider
        _logger.LogDebug("Feature flag cache miss: {FlagName}", featureName);

        int value = defaultValue;
        Exception? error = null;
        bool fromFlipt = false;

        try
        {
            value = await _innerService.GetIntValueAsync(featureName, defaultValue, context);
            fromFlipt = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feature flag from provider, using default: {FlagName} = {DefaultValue}",
                featureName, defaultValue);
            error = ex;
            _cacheService.RecordError();
        }

        // Store in cache
        var entry = new FeatureFlagCacheEntry
        {
            Value = value,
            FlagType = "int",
            FromFlipt = fromFlipt,
            Error = error
        };

        _cacheService.Set(featureName, entry);

        return value;
    }

    /// <summary>
    /// Get double feature value (with caching)
    /// </summary>
    public async Task<double> GetDoubleValueAsync(string featureName, double defaultValue, EvaluationContext? context = null)
    {
        // Try cache first
        var cached = _cacheService.Get(featureName);
        if (cached != null && cached.FlagType == "double")
        {
            _logger.LogDebug("Feature flag cache hit: {FlagName} = {Value}", featureName, cached.Value);
            return (double)(cached.Value ?? defaultValue);
        }

        // Cache miss - fetch from provider
        _logger.LogDebug("Feature flag cache miss: {FlagName}", featureName);

        double value = defaultValue;
        Exception? error = null;
        bool fromFlipt = false;

        try
        {
            value = await _innerService.GetDoubleValueAsync(featureName, defaultValue, context);
            fromFlipt = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feature flag from provider, using default: {FlagName} = {DefaultValue}",
                featureName, defaultValue);
            error = ex;
            _cacheService.RecordError();
        }

        // Store in cache
        var entry = new FeatureFlagCacheEntry
        {
            Value = value,
            FlagType = "double",
            FromFlipt = fromFlipt,
            Error = error
        };

        _cacheService.Set(featureName, entry);

        return value;
    }

    /// <summary>
    /// Get cache service for invalidation
    /// </summary>
    public FeatureFlagCacheService GetCacheService() => _cacheService;

    /// <summary>
    /// Invalidate all cache
    /// </summary>
    public void InvalidateCache()
    {
        _logger.LogInformation("Invalidating feature flag cache");
        _cacheService.Clear();
    }

    /// <summary>
    /// Invalidate specific flag
    /// </summary>
    public void InvalidateFlag(string flagName)
    {
        _logger.LogInformation("Invalidating feature flag: {FlagName}", flagName);
        _cacheService.Delete(flagName);
    }

    /// <summary>
    /// Warmup cache with configured flags
    /// </summary>
    public async Task WarmupCacheAsync()
    {
        if (!_cacheOptions.Enabled || _cacheOptions.WarmupFlags.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Warming up feature flag cache with {Count} flags", _cacheOptions.WarmupFlags.Count);

        foreach (var flag in _cacheOptions.WarmupFlags)
        {
            try
            {
                // Try to fetch as boolean (most common type)
                await IsEnabledAsync(flag, false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warmup flag: {FlagName}", flag);
            }
        }

        _logger.LogInformation("Feature flag cache warmup completed");
    }
}
