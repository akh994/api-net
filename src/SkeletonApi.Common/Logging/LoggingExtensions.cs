using Microsoft.Extensions.Logging;

namespace SkeletonApi.Common.Logging;

/// <summary>
/// Logging extensions for structured logging
/// </summary>
public static class LoggingExtensions
{
    public static IDisposable? BeginScope(
        this ILogger logger,
        string name,
        object value)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            [name] = value
        });
    }

    public static IDisposable? BeginScope(
        this ILogger logger,
        params (string Name, object Value)[] properties)
    {
        var dictionary = properties.ToDictionary(p => p.Name, p => p.Value);
        return logger.BeginScope(dictionary);
    }

    public static void LogTrace(
        this ILogger logger,
        string message,
        params (string Name, object Value)[] properties)
    {
        using (logger.BeginScope(properties))
        {
            logger.LogTrace(message);
        }
    }

    public static void LogDebug(
        this ILogger logger,
        string message,
        params (string Name, object Value)[] properties)
    {
        using (logger.BeginScope(properties))
        {
            logger.LogDebug(message);
        }
    }

    public static void LogInformation(
        this ILogger logger,
        string message,
        params (string Name, object Value)[] properties)
    {
        using (logger.BeginScope(properties))
        {
            logger.LogInformation(message);
        }
    }

    public static void LogWarning(
        this ILogger logger,
        string message,
        params (string Name, object Value)[] properties)
    {
        using (logger.BeginScope(properties))
        {
            logger.LogWarning(message);
        }
    }

    public static void LogError(
        this ILogger logger,
        Exception exception,
        string message,
        params (string Name, object Value)[] properties)
    {
        using (logger.BeginScope(properties))
        {
            logger.LogError(exception, message);
        }
    }
}
