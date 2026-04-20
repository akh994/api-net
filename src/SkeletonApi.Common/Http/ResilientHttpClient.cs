using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace SkeletonApi.Common.Http;

/// <summary>
/// HTTP client wrapper with retry and circuit breaker support
/// </summary>
public class ResilientHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResilientHttpClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _circuitBreakerPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    public ResilientHttpClient(
        HttpClient httpClient,
        ILogger<ResilientHttpClient> logger,
        HttpClientOptions? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;

        options ??= new HttpClientOptions();

        // Build circuit breaker pipeline
        _circuitBreakerPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = options.CircuitBreakerFailureThreshold,
                MinimumThroughput = options.CircuitBreakerMinThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnOpened = args =>
                {
                    _logger.LogError("Circuit breaker opened due to failures");
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker closed, resuming normal operation");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // Build retry pipeline
        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.MaxRetries,
                Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry attempt {Attempt} after {Delay}ms due to: {Reason}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        return await _retryPipeline.ExecuteAsync(async ct =>
        {
            return await _circuitBreakerPipeline.ExecuteAsync(
                async ct2 => await _httpClient.GetAsync(requestUri, ct2), ct);
        }, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
    {
        return await _circuitBreakerPipeline.ExecuteAsync(
            async ct => await _httpClient.PostAsync(requestUri, content, ct),
            cancellationToken);
    }

    public async Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
    {
        return await _circuitBreakerPipeline.ExecuteAsync(
            async ct => await _httpClient.PutAsync(requestUri, content, ct),
            cancellationToken);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        return await _retryPipeline.ExecuteAsync(async ct =>
        {
            return await _circuitBreakerPipeline.ExecuteAsync(
                async ct2 => await _httpClient.DeleteAsync(requestUri, ct2), ct);
        }, cancellationToken);
    }

    public async Task<T?> GetAsync<T>(string requestUri, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAndUnwrapAsync<T>(response, cancellationToken);
    }

    public async Task<T?> PostAsync<T>(string requestUri, object content, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync(requestUri, JsonContent.Create(content), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAndUnwrapAsync<T>(response, cancellationToken);
    }

    private async Task<T?> ReadAndUnwrapAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrEmpty(content)) return default;

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Try deserialize as ApiResponse<T> wrapper first
        try
        {
            var wrappedResponse = System.Text.Json.JsonSerializer.Deserialize<Response.ApiResponse<T>>(content, jsonOptions);
            if (wrappedResponse != null && wrappedResponse.Success)
            {
                return wrappedResponse.Data;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not a wrapped response, fall through to raw deserialization
        }

        // Fallback: deserialize as raw T
        return System.Text.Json.JsonSerializer.Deserialize<T>(content, jsonOptions);
    }
}

public class HttpClientOptions
{
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public double CircuitBreakerFailureThreshold { get; set; } = 0.5;
    public int CircuitBreakerMinThroughput { get; set; } = 10;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}
