using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.FeatureFlags;
using Xunit;

namespace SkeletonApi.Tests.Unit.FeatureFlags;

public class FeatureFlagCacheTests
{
    private readonly Mock<IMemoryCache> _mockCache;
    private readonly Mock<ICacheEntry> _mockCacheEntry;
    private readonly Mock<IOptions<FeatureFlagOptions>> _mockOptions;
    private readonly FeatureFlagCacheService _service;
    private readonly FeatureFlagOptions _options;

    public FeatureFlagCacheTests()
    {
        _mockCache = new Mock<IMemoryCache>();
        _mockCacheEntry = new Mock<ICacheEntry>();
        _mockOptions = new Mock<IOptions<FeatureFlagOptions>>();

        _options = new FeatureFlagOptions
        {
            Cache = new FeatureFlagCacheOptions
            {
                Enabled = true,
                TtlSeconds = 60,
                MetricsEnabled = false // Disable metrics for unit tests to avoid static state issues
            }
        };

        _mockOptions.Setup(x => x.Value).Returns(_options);

        // Setup cache entry mock
        _mockCache.Setup(m => m.CreateEntry(It.IsAny<object>()))
            .Returns(_mockCacheEntry.Object);

        // Setup CreateEntry to return the mock entry and setup Value property
        _mockCacheEntry.SetupProperty(e => e.Value);
        _mockCacheEntry.SetupProperty(e => e.AbsoluteExpirationRelativeToNow);
        _mockCacheEntry.SetupProperty(e => e.SlidingExpiration);
        _mockCacheEntry.Setup(e => e.ExpirationTokens).Returns(new List<Microsoft.Extensions.Primitives.IChangeToken>());
        _mockCacheEntry.SetupProperty(e => e.Priority);

        // Setup read-only PostEvictionCallbacks
        _mockCacheEntry.Setup(e => e.PostEvictionCallbacks).Returns(new List<PostEvictionCallbackRegistration>());

        _service = new FeatureFlagCacheService(_mockCache.Object, _mockOptions.Object);
    }

    [Fact]
    public void Get_ShouldReturnNull_WhenCacheDisabled()
    {
        // Arrange
        _options.Cache.Enabled = false;

        // Act
        var result = _service.Get("test-key");

        // Assert
        result.Should().BeNull();
        _mockCache.Verify(x => x.TryGetValue(It.IsAny<object>(), out It.Ref<object?>.IsAny), Times.Never);
    }

    [Fact]
    public void Get_ShouldReturnNull_WhenKeyNotFound()
    {
        // Arrange
        object? expectedValue = null;
        _mockCache.Setup(x => x.TryGetValue(It.IsAny<object>(), out expectedValue))
            .Returns(false);

        // Act
        var result = _service.Get("test-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Get_ShouldReturnEntry_WhenKeyFoundAndNotExpired()
    {
        // Arrange
        var entry = new FeatureFlagCacheEntry
        {
            Value = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        object? cacheValue = entry;

        _mockCache.Setup(x => x.TryGetValue("test-key", out cacheValue))
            .Returns(true);

        // Act
        var result = _service.Get("test-key");

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be(true);
    }

    [Fact]
    public void Get_ShouldReturnNullAndRemove_WhenKeyFoundButExpired()
    {
        // Arrange
        var entry = new FeatureFlagCacheEntry
        {
            Value = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1) // Expired
        };
        object? cacheValue = entry;

        _mockCache.Setup(x => x.TryGetValue("test-key", out cacheValue))
            .Returns(true);

        // Act
        var result = _service.Get("test-key");

        // Assert
        result.Should().BeNull();
        _mockCache.Verify(x => x.Remove("test-key"), Times.Once);
    }

    [Fact]
    public void Set_ShouldNotSet_WhenCacheDisabled()
    {
        // Arrange
        _options.Cache.Enabled = false;
        var entry = new FeatureFlagCacheEntry { Value = true };

        // Act
        _service.Set("test-key", entry);

        // Assert
        _mockCache.Verify(x => x.CreateEntry(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void Set_ShouldSetCacheEntry_WhenEnabled()
    {
        // Arrange
        var entry = new FeatureFlagCacheEntry { Value = true };

        // Act
        _service.Set("test-key", entry);

        // Assert
        _mockCache.Verify(x => x.CreateEntry("test-key"), Times.Once);
        entry.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(60), TimeSpan.FromSeconds(1));
        entry.FetchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Delete_ShouldRemoveFromCache()
    {
        // Act
        _service.Delete("test-key");

        // Assert
        _mockCache.Verify(x => x.Remove("test-key"), Times.Once);
    }

    [Fact]
    public void GetStatistics_ShouldReturnCorrectStats()
    {
        // Arrange
        // We need to simulate adding items to track size since internal counter is used
        var entry = new FeatureFlagCacheEntry { Value = true };
        _service.Set("key1", entry);
        _service.Set("key2", entry);

        // Act
        var stats = _service.GetStatistics();

        // Assert
        stats.Enabled.Should().BeTrue();
        stats.TotalEntries.Should().Be(2);
        stats.TtlSeconds.Should().Be(60);
    }
}
