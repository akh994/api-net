using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenFeature.Model;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.FeatureFlags;
using Xunit;

namespace SkeletonApi.Tests.Unit.FeatureFlags;

public class CachedFeatureFlagServiceTests
{
    private readonly Mock<FeatureFlagService> _mockFeatureService;
    private readonly Mock<FeatureFlagCacheService> _mockCacheService;
    private readonly Mock<IOptions<FeatureFlagOptions>> _mockOptions;
    private readonly Mock<ILogger<CachedFeatureFlagService>> _mockLogger;
    private readonly CachedFeatureFlagService _service;

    public CachedFeatureFlagServiceTests()
    {
        _mockFeatureService = new Mock<FeatureFlagService>();

        // Setup cache service mock
        var mockCache = new Mock<IMemoryCache>();
        _mockOptions = new Mock<IOptions<FeatureFlagOptions>>();
        _mockOptions.Setup(x => x.Value).Returns(new FeatureFlagOptions
        {
            Cache = new FeatureFlagCacheOptions { Enabled = true, MetricsEnabled = false }
        });

        _mockCacheService = new Mock<FeatureFlagCacheService>(mockCache.Object, _mockOptions.Object);
        _mockLogger = new Mock<ILogger<CachedFeatureFlagService>>();

        _service = new CachedFeatureFlagService(
            _mockFeatureService.Object,
            _mockCacheService.Object,
            _mockOptions.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldReturnCachedValue_WhenCacheHit()
    {
        // Arrange
        var cacheEntry = new FeatureFlagCacheEntry
        {
            Value = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(1),
            FlagType = "boolean"
        };

        _mockCacheService.Setup(x => x.Get("test-flag"))
            .Returns(cacheEntry);

        // Act
        var result = await _service.IsEnabledAsync("test-flag");

        // Assert
        result.Should().BeTrue();
        _mockFeatureService.Verify(x => x.IsEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<EvaluationContext>()), Times.Never);
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldFetchFromProvider_WhenCacheMiss()
    {
        // Arrange
        _mockCacheService.Setup(x => x.Get("test-flag"))
            .Returns((FeatureFlagCacheEntry?)null);

        _mockFeatureService.Setup(x => x.IsEnabledAsync("test-flag", false, null))
            .ReturnsAsync(true);

        // Act
        var result = await _service.IsEnabledAsync("test-flag");

        // Assert
        result.Should().BeTrue();
        _mockFeatureService.Verify(x => x.IsEnabledAsync("test-flag", false, null), Times.Once);
        _mockCacheService.Verify(x => x.Set("test-flag", It.Is<FeatureFlagCacheEntry>(e => (bool)e.Value! == true)), Times.Once);
    }

    [Fact]
    public async Task IsEnabledAsync_ShouldReturnDefault_WhenProviderFails()
    {
        // Arrange
        _mockCacheService.Setup(x => x.Get("test-flag"))
            .Returns((FeatureFlagCacheEntry?)null);

        _mockFeatureService.Setup(x => x.IsEnabledAsync("test-flag", true, null))
            .ThrowsAsync(new InvalidOperationException("Provider error"));

        // Act
        var result = await _service.IsEnabledAsync("test-flag", true); // Default true

        // Assert
        result.Should().BeTrue(); // Should return default value (which we passed as true)
    }

    [Fact]
    public async Task GetStringValueAsync_ShouldReturnCachedValue_WhenCacheHit()
    {
        // Arrange
        var cacheEntry = new FeatureFlagCacheEntry
        {
            Value = "cached-value",
            ExpiresAt = DateTime.UtcNow.AddMinutes(1),
            FlagType = "string"
        };

        _mockCacheService.Setup(x => x.Get("test-string"))
            .Returns(cacheEntry);

        // Act
        var result = await _service.GetStringValueAsync("test-string", "default");

        // Assert
        result.Should().Be("cached-value");
        _mockFeatureService.Verify(x => x.GetStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EvaluationContext>()), Times.Never);
    }

    [Fact]
    public void InvalidateCache_ShouldCallCacheClear()
    {
        // Act
        _service.InvalidateCache();

        // Assert
        _mockCacheService.Verify(x => x.Clear(), Times.Once);
    }

    [Fact]
    public void InvalidateFlag_ShouldCallCacheDelete()
    {
        // Act
        _service.InvalidateFlag("test-flag");

        // Assert
        _mockCacheService.Verify(x => x.Delete("test-flag"), Times.Once);
    }
}
