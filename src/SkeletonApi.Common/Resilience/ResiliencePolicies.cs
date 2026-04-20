using System.Net.Http;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.Resilience;

/// <summary>
/// Pre-configured resilience policies for common scenarios
/// </summary>
public static class ResiliencePolicies
{
    private static bool IsFailure<T>(Outcome<T> outcome)
    {
        if (outcome.Exception is RpcException rpcEx)
        {
            // Only fail on system errors
            return rpcEx.StatusCode != StatusCode.InvalidArgument &&
                   rpcEx.StatusCode != StatusCode.NotFound &&
                   rpcEx.StatusCode != StatusCode.AlreadyExists &&
                   rpcEx.StatusCode != StatusCode.PermissionDenied &&
                   rpcEx.StatusCode != StatusCode.Unauthenticated &&
                   rpcEx.StatusCode != StatusCode.FailedPrecondition &&
                   rpcEx.StatusCode != StatusCode.OutOfRange &&
                   rpcEx.StatusCode != StatusCode.Unimplemented;
        }

        if (outcome.Exception != null)
        {
            return true;
        }

        if (outcome.Result is HttpResponseMessage resp)
        {
            return (int)resp.StatusCode >= 500;
        }

        return false;
    }

    /// <summary>
    /// Creates a retry policy with exponential backoff
    /// </summary>
    public static ResiliencePipeline<T> CreateRetryPolicy<T>(
        int maxRetries = 3,
        int initialDelayMs = 100)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(initialDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args => new ValueTask<bool>(IsFailure(args.Outcome))
            })
            .Build();
    }

    /// <summary>
    /// Creates a circuit breaker policy
    /// </summary>
    public static ResiliencePipeline<T> CreateCircuitBreakerPolicy<T>(
        double failureThreshold = 0.5,
        int minimumThroughput = 10,
        int durationSeconds = 30)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio = failureThreshold,
                MinimumThroughput = minimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(durationSeconds),
                ShouldHandle = args => new ValueTask<bool>(IsFailure(args.Outcome))
            })
            .Build();
    }

    /// <summary>
    /// Creates a combined retry + circuit breaker policy
    /// </summary>
    public static ResiliencePipeline<T> CreateCombinedPolicy<T>(
        int maxRetries = 3,
        int initialDelayMs = 100,
        double failureThreshold = 0.5,
        int minimumThroughput = 10,
        int durationSeconds = 30)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(initialDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args => new ValueTask<bool>(IsFailure(args.Outcome))
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio = failureThreshold,
                MinimumThroughput = minimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(durationSeconds),
                ShouldHandle = args => new ValueTask<bool>(IsFailure(args.Outcome))
            })
            .Build();
    }

    /// <summary>
    /// Creates a timeout policy
    /// </summary>
    public static ResiliencePipeline<T> CreateTimeoutPolicy<T>(int timeoutSeconds = 30)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddTimeout(TimeSpan.FromSeconds(timeoutSeconds))
            .Build();
    }
}
