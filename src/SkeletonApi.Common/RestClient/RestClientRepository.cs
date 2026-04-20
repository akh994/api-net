using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.RestClient;

/// <summary>
/// Generic REST client repository with resilience
/// </summary>
public class RestClientRepository : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RestClientRepository> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _circuitBreakerPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public RestClientRepository(RestClientOptions options, ILogger<RestClientRepository> logger, HttpClient? httpClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var tls = options.GetTlsForUserService();
        if (httpClient == null)
        {
            var handler = new HttpClientHandler();
            if (tls.Enabled)
            {
                if (tls.InsecureSkipVerify)
                {
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                if (!string.IsNullOrEmpty(tls.CertFile) && !string.IsNullOrEmpty(tls.KeyFile))
                {
                    var cert = X509Certificate2.CreateFromPemFile(tls.CertFile, tls.KeyFile);
                    handler.ClientCertificates.Add(cert);
                }
            }
            _httpClient = new HttpClient(handler);
        }
        else
        {
            _httpClient = httpClient;
        }

        _httpClient.BaseAddress = new Uri(options.UserService.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(options.UserService.TimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var cb = options.GetCircuitBreakerForUserService();

        // Build resilience pipelines
        _circuitBreakerPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = cb.FailureRatio,
                MinimumThroughput = cb.MinRequests,
                BreakDuration = TimeSpan.FromSeconds(cb.TimeoutSeconds),
                SamplingDuration = TimeSpan.FromSeconds(cb.IntervalSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnOpened = args =>
                {
                    _logger.LogError("REST circuit breaker opened");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.Retry.MaxAttempts,
                Delay = TimeSpan.FromMilliseconds(options.Retry.DelayMs),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnRetry = args =>
                {
                    _logger.LogWarning("REST retry attempt {Attempt} due to: {Reason}",
                        args.AttemptNumber, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        _logger.LogInformation("REST client initialized with base URL: {BaseUrl}", options.UserService.BaseUrl);
    }

    /// <summary>
    /// GET request
    /// </summary>
    public async Task<TResponse?> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await _retryPipeline.ExecuteAsync(async ct =>
        {
            return await _circuitBreakerPipeline.ExecuteAsync(async ct2 =>
            {
                return await _httpClient.GetAsync(endpoint, ct2);
            }, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    /// <summary>
    /// POST request
    /// </summary>
    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken cancellationToken = default)
    {
        var response = await _circuitBreakerPipeline.ExecuteAsync(async ct =>
        {
            return await _httpClient.PostAsJsonAsync(endpoint, data, _jsonOptions, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    /// <summary>
    /// PUT request
    /// </summary>
    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken cancellationToken = default)
    {
        var response = await _circuitBreakerPipeline.ExecuteAsync(async ct =>
        {
            return await _httpClient.PutAsJsonAsync(endpoint, data, _jsonOptions, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    /// <summary>
    /// DELETE request
    /// </summary>
    public async Task DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await _retryPipeline.ExecuteAsync(async ct =>
        {
            return await _circuitBreakerPipeline.ExecuteAsync(async ct2 =>
            {
                return await _httpClient.DeleteAsync(endpoint, ct2);
            }, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// PATCH request
    /// </summary>
    public async Task<TResponse?> PatchAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken cancellationToken = default)
    {
        var response = await _circuitBreakerPipeline.ExecuteAsync(async ct =>
        {
            return await _httpClient.PatchAsJsonAsync(endpoint, data, _jsonOptions, ct);
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    /// <summary>
    /// Send custom HTTP request
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete)
        {
            var response = await _retryPipeline.ExecuteAsync(async ct =>
            {
                return await _circuitBreakerPipeline.ExecuteAsync(async ct2 =>
                {
                    return await _httpClient.SendAsync(request, ct2);
                }, ct);
            }, cancellationToken);

            response.EnsureSuccessStatusCode();
            return response;
        }
        else
        {
            var response = await _circuitBreakerPipeline.ExecuteAsync(async ct =>
            {
                return await _httpClient.SendAsync(request, ct);
            }, cancellationToken);

            response.EnsureSuccessStatusCode();
            return response;
        }
    }

    /// <summary>
    /// Get the underlying HttpClient for advanced scenarios
    /// </summary>
    public HttpClient GetHttpClient() => _httpClient;

    public void Dispose()
    {
        if (_disposed) return;

        if (_httpClient != null)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
        _logger.LogInformation("REST client disposed");
    }
}
