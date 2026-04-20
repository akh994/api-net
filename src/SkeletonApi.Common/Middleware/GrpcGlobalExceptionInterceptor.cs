using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Errors;

namespace SkeletonApi.Common.Middleware;

/// <summary>
/// Global exception interceptor for gRPC services
/// </summary>
public class GrpcGlobalExceptionInterceptor : Interceptor
{
    private readonly ILogger<GrpcGlobalExceptionInterceptor> _logger;

    public GrpcGlobalExceptionInterceptor(ILogger<GrpcGlobalExceptionInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC error in {Method}", context.Method);
            throw MapToRpcException(ex);
        }
    }

    private RpcException MapToRpcException(Exception ex)
    {
        return ex switch
        {
            RpcException rpcEx => rpcEx,

            SkeletonApi.Common.Errors.ValidationException valEx => new RpcException(new Status(StatusCode.InvalidArgument, valEx.Message)),

            FluentValidation.ValidationException fluentEx => new RpcException(new Status(StatusCode.InvalidArgument, fluentEx.Message)),

            NotFoundException notFoundEx => new RpcException(new Status(StatusCode.NotFound, notFoundEx.Message)),

            UnauthorizedException unauthEx => new RpcException(new Status(StatusCode.Unauthenticated, unauthEx.Message)),

            ForbiddenException forbidEx => new RpcException(new Status(StatusCode.PermissionDenied, forbidEx.Message)),

            ConflictException conflictEx => new RpcException(new Status(StatusCode.AlreadyExists, conflictEx.Message)),

            DataAccessHubException dataEx => new RpcException(new Status(StatusCode.Internal, dataEx.Message)),

            BadRequestException badReqEx => new RpcException(new Status(StatusCode.InvalidArgument, badReqEx.Message)),

            KeyNotFoundException keyEx => new RpcException(new Status(StatusCode.NotFound, keyEx.Message)),

            _ => new RpcException(new Status(StatusCode.Internal, "An internal error occurred"))
        };
    }
}
