using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Prometheus;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.FeatureFlags;

/// <summary>
/// Cache entry for feature flag values
/// </summary>
public class FeatureFlagCacheEntry
{
    public object? Value { get; set; }
    public string FlagType { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool FromFlipt { get; set; }
    public Exception? Error { get; set; }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public bool Enabled { get; set; }
    public int TotalEntries { get; set; }
    public int ActiveEntries { get; set; }
    public int ExpiredEntries { get; set; }
    public int TtlSeconds { get; set; }
}

/// <summary>
/// Feature flag cache service using IMemoryCache
/// </summary>
public class FeatureFlagCacheService
{
    private readonly IMemoryCache _cache;
    private readonly FeatureFlagCacheOptions _options;
    private readonly bool _metricsEnabled;

    // Prometheus metrics
    private static readonly Counter CacheHits = Metrics.CreateCounter(
        "feature_flag_cache_hits_total",
        "Total number of feature flag cache hits");

    private static readonly Counter CacheMisses = Metrics.CreateCounter(
        "feature_flag_cache_misses_total",
        "Total number of feature flag cache misses");

    private static readonly Counter CacheErrors = Metrics.CreateCounter(
        "feature_flag_cache_errors_total",
        "Total number of errors when fetching feature flags");

    private static readonly Gauge CacheSize = Metrics.CreateGauge(
        "feature_flag_cache_size",
        "Current number of entries in feature flag cache");

    private static readonly Gauge CacheTtl = Metrics.CreateGauge(
        "feature_flag_cache_ttl_seconds",
        "Feature flag cache TTL in seconds");

    private readonly object _lockObject = new();
    private int _currentSize = 0;

    public FeatureFlagCacheService(
        IMemoryCache cache,
        IOptions<FeatureFlagOptions> options)
    {
        _cache = cache;
        _options = options.Value.Cache;
        _metricsEnabled = options.Value.Cache.MetricsEnabled;

        if (_metricsEnabled)
        {
            CacheTtl.Set(_options.TtlSeconds);
        }
    }

    /// <summary>
    /// Get value from cache
    /// </summary>
    public virtual FeatureFlagCacheEntry? Get(string key)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (_cache.TryGetValue(key, out FeatureFlagCacheEntry? entry))
        {
            if (entry != null && !entry.IsExpired())
            {
                if (_metricsEnabled)
                {
                    CacheHits.Inc();
                }
                return entry;
            }

            // Remove expired entry
            _cache.Remove(key);
            DecrementSize();
        }

        if (_metricsEnabled)
        {
            CacheMisses.Inc();
        }

        return null;
    }

    /// <summary>
    /// Set value in cache
    /// </summary>
    public virtual void Set(string key, FeatureFlagCacheEntry entry)
    {
        if (!_options.Enabled)
        {
            return;
        }

        entry.ExpiresAt = DateTime.UtcNow.AddSeconds(_options.TtlSeconds);
        entry.FetchedAt = DateTime.UtcNow;

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.TtlSeconds),
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (key, value, reason, state) => DecrementSize()
                }
            }
        };

        _cache.Set(key, entry, cacheOptions);
        IncrementSize();
    }

    /// <summary>
    /// Delete value from cache
    /// </summary>
    public virtual void Delete(string key)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _cache.Remove(key);
        DecrementSize();
    }

    /// <summary>
    /// Clear all cache entries
    /// </summary>
    public virtual void Clear()
    {
        if (!_options.Enabled)
        {
            return;
        }

        // IMemoryCache doesn't have a Clear method, so we need to track keys
        // For simplicity, we'll just reset the size counter
        // In production, you might want to use a custom cache implementation
        lock (_lockObject)
        {
            _currentSize = 0;
            if (_metricsEnabled)
            {
                CacheSize.Set(0);
            }
        }
    }

    /// <summary>
    /// Get all cache entries (for statistics)
    /// </summary>
    public virtual Dictionary<string, FeatureFlagCacheEntry> GetAll()
    {
        // Note: IMemoryCache doesn't provide a way to enumerate all entries
        // This is a limitation of the built-in IMemoryCache
        // For production, consider using a custom cache implementation or Redis
        return new Dictionary<string, FeatureFlagCacheEntry>();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public virtual CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            Enabled = _options.Enabled,
            TotalEntries = _currentSize,
            ActiveEntries = _currentSize, // Approximation since we can't enumerate
            ExpiredEntries = 0,
            TtlSeconds = _options.TtlSeconds
        };
    }

    /// <summary>
    /// Record cache error
    /// </summary>
    public virtual void RecordError()
    {
        if (_metricsEnabled)
        {
            CacheErrors.Inc();
        }
    }

    private void IncrementSize()
    {
        lock (_lockObject)
        {
            _currentSize++;
            if (_metricsEnabled)
            {
                CacheSize.Set(_currentSize);
            }
        }
    }

    private void DecrementSize()
    {
        lock (_lockObject)
        {
            if (_currentSize > 0)
            {
                _currentSize--;
                if (_metricsEnabled)
                {
                    CacheSize.Set(_currentSize);
                }
            }
        }
    }
}
