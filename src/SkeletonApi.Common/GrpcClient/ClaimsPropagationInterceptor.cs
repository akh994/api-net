using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Interfaces;

namespace SkeletonApi.Common.GrpcClient;

/// <summary>
/// Interceptor to propagate user claims to downstream gRPC services
/// </summary>
public class ClaimsPropagationInterceptor : Interceptor
{
    private readonly IUserContext _userContext;
    private readonly Microsoft.Extensions.Logging.ILogger<ClaimsPropagationInterceptor> _logger;

    public ClaimsPropagationInterceptor(IUserContext userContext, Microsoft.Extensions.Logging.ILogger<ClaimsPropagationInterceptor> logger)
    {
        _userContext = userContext;
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        InjectClaims(metadata);

        var newOptions = context.Options.WithHeaders(metadata);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }

    private void InjectClaims(Metadata metadata)
    {
        var claims = _userContext.GetCurrentUser();

        // Propagate standard claims
        if (!string.IsNullOrEmpty(claims.UserId))
        {
            metadata.Add("x-user-id", claims.UserId);
        }

        if (claims.UserUuid.HasValue)
        {
            metadata.Add("x-user-uuid", claims.UserUuid.Value.ToString());
        }

        if (claims.DomainId.HasValue)
        {
            metadata.Add("x-domain-id", claims.DomainId.Value.ToString());
        }

        if (!string.IsNullOrEmpty(claims.DomainName))
        {
            metadata.Add("x-domain-name", claims.DomainName);
        }

        if (!string.IsNullOrEmpty(claims.DomainType))
        {
            metadata.Add("x-domain-type", claims.DomainType);
        }

        if (!string.IsNullOrEmpty(claims.GroupName))
        {
            metadata.Add("x-group-name", claims.GroupName);
        }

        if (!string.IsNullOrEmpty(claims.Email))
        {
            metadata.Add("x-user-email", claims.Email);
        }

        if (!string.IsNullOrEmpty(claims.PhoneNumber))
        {
            metadata.Add("x-phone-number", claims.PhoneNumber);
        }

        if (!string.IsNullOrEmpty(claims.Role))
        {
            metadata.Add("x-user-role", claims.Role);
        }

        // Propagate extra attributes as JSON
        var extraAttributesJson = claims.SerializeExtraAttributes();
        if (!string.IsNullOrEmpty(extraAttributesJson))
        {
            metadata.Add("x-extra-attributes", extraAttributesJson);
        }

        // Backward compatibility - also send x-user-name
        if (!string.IsNullOrEmpty(claims.UserId))
        {
            metadata.Add("x-user-name", claims.UserId);
        }

        _logger.LogDebug("Interceptor: Injected claims to gRPC metadata. UserId: {UserId}, Role: {Role}", claims.UserId, claims.Role);
    }
}
