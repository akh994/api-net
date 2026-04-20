using Microsoft.AspNetCore.Mvc;
using SkeletonApi.Common.FeatureFlags;

namespace SkeletonApi.Endpoints;

/// <summary>
/// Feature flag endpoint for exposing feature flag values
/// </summary>
public static class FeatureFlagEndpoint
{
    /// <summary>
    /// Maps the feature flag endpoints
    /// </summary>
    public static void MapFeatureFlagEndpoint(this IEndpointRouteBuilder app, int? port = null)
    {
        var endpoints = new List<IEndpointConventionBuilder>();

        endpoints.Add(app.MapGet("/api/v1/feature-flags", GetFeatureFlags)
            .WithName("GetFeatureFlags")
            .WithTags("FeatureFlags")
            .Produces<Dictionary<string, object>>(StatusCodes.Status200OK));

        endpoints.Add(app.MapGet("/api/v1/feature-flags/stats", GetCacheStats)
            .WithName("GetFeatureFlagCacheStats")
            .WithTags("FeatureFlags")
            .Produces<object>(StatusCodes.Status200OK));

        endpoints.Add(app.MapPost("/api/v1/feature-flags/cache/invalidate", InvalidateCache)
            .WithName("InvalidateFeatureFlagCache")
            .WithTags("FeatureFlags")
            .Produces<object>(StatusCodes.Status200OK));

        endpoints.Add(app.MapPost("/api/v1/feature-flags/cache/invalidate-flag", InvalidateFlag)
            .WithName("InvalidateFeatureFlag")
            .WithTags("FeatureFlags")
            .Produces<object>(StatusCodes.Status200OK));

        if (port.HasValue)
        {
            foreach (var endpoint in endpoints)
            {
                endpoint.RequireHost($"*:{port.Value}");
            }
        }
    }

    /// <summary>
    /// Get all feature flag values
    /// </summary>
    private static async Task<IResult> GetFeatureFlags(
        [FromServices] CachedFeatureFlagService featureFlagService,
        [FromServices] ILogger<CachedFeatureFlagService> logger)
    {
        // List of flags to check (matching skeleton-api-go)
        var flagKeys = new[]
        {
            "grpc-client",
            "new-user-processing",
            "enhanced-email-notifications",
            "database-migration-mode",
            "maintenance-mode",
            "new-dashboard"
        };

        var flags = new Dictionary<string, object>();

        foreach (var key in flagKeys)
        {
            try
            {
                // Evaluate boolean flags
                var value = await featureFlagService.IsEnabledAsync(key, false);
                flags[key] = value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to evaluate flag {FlagKey}", key);
                flags[key] = "error";
            }
        }

        // Also check non-boolean flags
        try
        {
            var apiVersion = await featureFlagService.GetStringValueAsync("api-version", "v1");
            flags["api-version"] = apiVersion;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate flag api-version");
            flags["api-version"] = "v1";
        }

        try
        {
            var maxConcurrent = await featureFlagService.GetIntValueAsync("max-concurrent-requests", 10);
            flags["max-concurrent-requests"] = maxConcurrent;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate flag max-concurrent-requests");
            flags["max-concurrent-requests"] = 10;
        }

        return Results.Ok(flags);
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    private static IResult GetCacheStats(
        [FromServices] CachedFeatureFlagService featureFlagService)
    {
        var cacheService = featureFlagService.GetCacheService();
        var stats = cacheService.GetStatistics();

        var response = new
        {
            enabled = stats.Enabled,
            total_entries = stats.TotalEntries,
            active_entries = stats.ActiveEntries,
            expired_entries = stats.ExpiredEntries,
            ttl_seconds = stats.TtlSeconds
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Invalidate all cache
    /// </summary>
    private static IResult InvalidateCache(
        [FromServices] CachedFeatureFlagService featureFlagService,
        [FromServices] ILogger<CachedFeatureFlagService> logger)
    {
        featureFlagService.InvalidateCache();
        logger.LogInformation("Feature flag cache invalidated via API");

        return Results.Ok(new { message = "Cache invalidated successfully" });
    }

    /// <summary>
    /// Invalidate specific flag
    /// </summary>
    private static IResult InvalidateFlag(
        [FromQuery] string flag,
        [FromServices] CachedFeatureFlagService featureFlagService,
        [FromServices] ILogger<CachedFeatureFlagService> logger)
    {
        if (string.IsNullOrEmpty(flag))
        {
            return Results.BadRequest(new { error = "flag parameter is required" });
        }

        featureFlagService.InvalidateFlag(flag);
        logger.LogInformation("Feature flag invalidated via API: {FlagName}", flag);

        return Results.Ok(new
        {
            message = "Flag invalidated successfully",
            flag
        });
    }
}
