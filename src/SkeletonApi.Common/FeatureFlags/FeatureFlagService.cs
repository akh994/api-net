using OpenFeature;
using OpenFeature.Model;

namespace SkeletonApi.Common.FeatureFlags;

/// <summary>
/// Feature flag service wrapper for OpenFeature
/// </summary>
public class FeatureFlagService
{
    private readonly FeatureClient _client;

    public FeatureFlagService()
    {
        _client = Api.Instance.GetClient();
    }

    /// <summary>
    /// Check if a feature is enabled
    /// </summary>
    public virtual async Task<bool> IsEnabledAsync(string featureName, bool defaultValue = false, EvaluationContext? context = null)
    {
        var result = await _client.GetBooleanValueAsync(featureName, defaultValue, context);
        return result;
    }

    /// <summary>
    /// Get string feature value
    /// </summary>
    public virtual async Task<string> GetStringValueAsync(string featureName, string defaultValue, EvaluationContext? context = null)
    {
        return await _client.GetStringValueAsync(featureName, defaultValue, context);
    }

    /// <summary>
    /// Get integer feature value
    /// </summary>
    public virtual async Task<int> GetIntValueAsync(string featureName, int defaultValue, EvaluationContext? context = null)
    {
        return await _client.GetIntegerValueAsync(featureName, defaultValue, context);
    }

    /// <summary>
    /// Get double feature value
    /// </summary>
    public virtual async Task<double> GetDoubleValueAsync(string featureName, double defaultValue, EvaluationContext? context = null)
    {
        return await _client.GetDoubleValueAsync(featureName, defaultValue, context);
    }

    /// <summary>
    /// Get object feature value
    /// </summary>
    public virtual async Task<Value> GetObjectValueAsync(string featureName, Value defaultValue, EvaluationContext? context = null)
    {
        return await _client.GetObjectValueAsync(featureName, defaultValue, context);
    }

    /// <summary>
    /// Get detailed evaluation result
    /// </summary>
    public virtual async Task<FlagEvaluationDetails<bool>> GetBooleanDetailsAsync(string featureName, bool defaultValue, EvaluationContext? context = null)
    {
        return await _client.GetBooleanDetailsAsync(featureName, defaultValue, context);
    }
}

/// <summary>
/// Feature flag extensions for easy usage
/// </summary>
public static class FeatureFlagExtensions
{
    /// <summary>
    /// Create evaluation context with user information
    /// </summary>
    public static EvaluationContext CreateContext(string? userId = null, Dictionary<string, Value>? attributes = null)
    {
        var builder = EvaluationContext.Builder();

        if (!string.IsNullOrEmpty(userId))
        {
            builder.SetTargetingKey(userId);
        }

        if (attributes != null)
        {
            foreach (var attr in attributes)
            {
                builder.Set(attr.Key, attr.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Create context with custom attributes
    /// </summary>
    public static EvaluationContext CreateContext(Dictionary<string, object> attributes)
    {
        var builder = EvaluationContext.Builder();

        foreach (var attr in attributes)
        {
            builder.Set(attr.Key, new Value(attr.Value));
        }

        return builder.Build();
    }
}
