using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SkeletonApi.Common.Configuration;

namespace SkeletonApi.Common.GrpcClient;

/// <summary>
/// Generic gRPC client repository with resilience
/// </summary>
public class GrpcClientRepository<TClient> : IDisposable where TClient : ClientBase<TClient>
{
    private readonly GrpcChannel _channel;
    private readonly TClient _client;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private bool _disposed;

    public GrpcClientRepository(
        string address,
        CircuitBreakerOptions cbOptions,
        TlsOptions tlsOptions,
        ILogger logger,
        Func<CallInvoker, TClient> clientFactory,
        ClaimsPropagationInterceptor? claimsInterceptor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var channelOptions = new GrpcChannelOptions();

        if (tlsOptions.Enabled)
        {
            var handler = new HttpClientHandler();
            if (tlsOptions.InsecureSkipVerify)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            if (!string.IsNullOrEmpty(tlsOptions.CertFile) && !string.IsNullOrEmpty(tlsOptions.KeyFile))
            {
                var cert = X509Certificate2.CreateFromPemFile(tlsOptions.CertFile, tlsOptions.KeyFile);
                handler.ClientCertificates.Add(cert);
            }

            channelOptions.HttpHandler = handler;
        }

        _channel = GrpcChannel.ForAddress(address, channelOptions);

        CallInvoker invoker = _channel.CreateCallInvoker();
        if (claimsInterceptor != null)
        {
            invoker = invoker.Intercept(claimsInterceptor);
        }

        _client = clientFactory(invoker);

        // Build resilience pipeline
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3, // Default to 3 as in Go
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                                  ex.StatusCode == StatusCode.DeadlineExceeded),
                OnRetry = args =>
                {
                    _logger.LogWarning("gRPC retry attempt {Attempt} after {Delay}ms",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = cbOptions.FailureRatio,
                MinimumThroughput = cbOptions.MinRequests,
                BreakDuration = TimeSpan.FromSeconds(cbOptions.TimeoutSeconds),
                SamplingDuration = TimeSpan.FromSeconds(cbOptions.IntervalSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(ex =>
                        ex.StatusCode != StatusCode.InvalidArgument &&
                        ex.StatusCode != StatusCode.NotFound &&
                        ex.StatusCode != StatusCode.AlreadyExists &&
                        ex.StatusCode != StatusCode.PermissionDenied &&
                        ex.StatusCode != StatusCode.Unauthenticated &&
                        ex.StatusCode != StatusCode.FailedPrecondition &&
                        ex.StatusCode != StatusCode.OutOfRange &&
                        ex.StatusCode != StatusCode.Unimplemented),
                OnOpened = args =>
                {
                    _logger.LogError("gRPC circuit breaker opened");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        _logger.LogInformation("gRPC client connected to: {Address}", address);
    }

    /// <summary>
    /// Execute gRPC call with resilience
    /// </summary>
    public async Task<TResponse> CallAsync<TResponse>(
        Func<TClient, Task<TResponse>> grpcCall,
        CancellationToken cancellationToken = default)
    {
        return await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            return await grpcCall(_client);
        }, cancellationToken);
    }

    /// <summary>
    /// Execute gRPC call with request parameter
    /// </summary>
    public async Task<TResponse> CallAsync<TRequest, TResponse>(
        Func<TClient, TRequest, Task<TResponse>> grpcCall,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            return await grpcCall(_client, request);
        }, cancellationToken);
    }

    /// <summary>
    /// Get the underlying gRPC client for advanced scenarios
    /// </summary>
    public TClient GetClient() => _client;

    public void Dispose()
    {
        if (_disposed) return;

        _channel?.Dispose();
        _disposed = true;
        _logger.LogInformation("gRPC client disposed");
    }
}
