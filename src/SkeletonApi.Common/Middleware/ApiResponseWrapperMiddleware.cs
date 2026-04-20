using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Http.Response;

namespace SkeletonApi.Common.Middleware;

/// <summary>
/// Middleware to wrap REST API responses with standardized ApiResponse format
/// </summary>
public class ApiResponseWrapperMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiResponseWrapperMiddleware> _logger;

    public ApiResponseWrapperMiddleware(RequestDelegate next, ILogger<ApiResponseWrapperMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only wrap REST API responses (not gRPC, not Swagger, not health checks)
        if (ShouldWrapResponse(context))
        {
            var originalBodyStream = context.Response.Body;

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            try
            {
                await _next(context);

                // Check for gRPC transcoding error responses (usually >= 400)
                if (context.Response.StatusCode >= 400 &&
                    context.Response.ContentType?.Contains("application/json") == true)
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    var responseText = await new StreamReader(responseBody).ReadToEndAsync();

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        // Check if it's a gRPC error format (contains "code" and "message", but not "success")
                        using var doc = JsonDocument.Parse(responseText);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("code", out var codeProp) &&
                            root.TryGetProperty("message", out var messageProp) &&
                            !root.TryGetProperty("success", out _))
                        {
                            var grpcCode = codeProp.GetInt32();
                            var grpcMessage = messageProp.GetString() ?? "Unknown error";
                            var traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

                            var errorCode = MapGrpcCodeToErrorCode(grpcCode);
                            var wrappedResponse = ApiResponseFactory.Error<object>(grpcMessage, code: errorCode, traceId: traceId);

                            var wrappedJson = JsonSerializer.Serialize(wrappedResponse, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            });

                            context.Response.ContentLength = null; // Reset content length
                            context.Response.Body = originalBodyStream;
                            await context.Response.WriteAsync(wrappedJson);
                            return;
                        }
                    }
                }

                // Only wrap successful responses with JSON content
                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300 &&
                    context.Response.ContentType?.Contains("application/json") == true)
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    var responseText = await new StreamReader(responseBody).ReadToEndAsync();

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        // Parse original response
                        var originalData = JsonSerializer.Deserialize<object>(responseText);
                        var traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

                        // Wrap with ApiResponse
                        var wrappedResponse = ApiResponseFactory.Success(
                            originalData,
                            GetSuccessMessage(context),
                            traceId: traceId
                        );

                        // Serialize wrapped response
                        var wrappedJson = JsonSerializer.Serialize(wrappedResponse, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        // Write wrapped response
                        context.Response.ContentLength = null; // Reset content length
                        context.Response.Body = originalBodyStream;
                        await context.Response.WriteAsync(wrappedJson);
                        return;
                    }
                }

                // For non-JSON or other responses, return as-is
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ApiResponseWrapperMiddleware");

                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("The response has already started, the error handler will not be executed.");
                    throw;
                }

                // Return error response
                context.Response.Body = originalBodyStream;
                context.Response.ContentType = "application/json";

                var responseModel = MapExceptionToResponse(ex, context);
                context.Response.StatusCode = responseModel.StatusCode;

                var errorJson = JsonSerializer.Serialize(responseModel.Response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await context.Response.WriteAsync(errorJson);
            }
        }
        else
        {
            await _next(context);
        }
    }

    private (int StatusCode, ApiResponse<object> Response) MapExceptionToResponse(Exception ex, HttpContext context)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

        return ex switch
        {
            // Validation Exceptions (400)
            SkeletonApi.Common.Errors.ValidationException valEx => (400, ApiResponseFactory.Error<object>(valEx.Message, errors: valEx.Errors, traceId: traceId)),
            FluentValidation.ValidationException fluentEx => (400, ApiResponseFactory.Error<object>("Validation failed", errors:
                fluentEx.Errors.GroupBy(e => e.PropertyName)
                               .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()), traceId: traceId)),
            SkeletonApi.Common.Errors.BadRequestException badReqEx => (400, ApiResponseFactory.Error<object>(badReqEx.Message, traceId: traceId)),

            // Not Found Exceptions (404)
            SkeletonApi.Common.Errors.NotFoundException notFoundEx => (404, ApiResponseFactory.Error<object>(notFoundEx.Message, traceId: traceId)),
            KeyNotFoundException keyEx => (404, ApiResponseFactory.Error<object>(keyEx.Message, traceId: traceId)),

            // Authentication/Authorization Exceptions (401/403)
            SkeletonApi.Common.Errors.UnauthorizedException unauthEx => (401, ApiResponseFactory.Error<object>(unauthEx.Message, traceId: traceId)),
            SkeletonApi.Common.Errors.ForbiddenException forbidEx => (403, ApiResponseFactory.Error<object>(forbidEx.Message, traceId: traceId)),

            // Conflict Exceptions (409)
            SkeletonApi.Common.Errors.ConflictException conflictEx => (409, ApiResponseFactory.Error<object>(conflictEx.Message, traceId: traceId)),

            // Default (500)
            _ => (500, ApiResponseFactory.InternalServerError("An error occurred while processing the request", traceId))
        };
    }

    private string MapGrpcCodeToErrorCode(int grpcCode)
    {
        // Map gRPC status codes to internal string error codes
        return grpcCode switch
        {
            3 => "BAD_REQUEST",         // INVALID_ARGUMENT
            5 => "NOT_FOUND",           // NOT_FOUND
            6 => "CONFLICT",            // ALREADY_EXISTS
            7 => "FORBIDDEN",           // PERMISSION_DENIED
            16 => "UNAUTHORIZED",       // UNAUTHENTICATED
            _ => "INTERNAL_SERVER_ERROR"
        };
    }

    private bool ShouldWrapResponse(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Don't wrap these paths
        if (path.StartsWith("/grpc") ||
            path.StartsWith("/swagger") ||
            path.StartsWith("/health") ||
            path == "/" ||
            path == "/favicon.ico")
        {
            return false;
        }

        // Only wrap REST API paths (v1/*)
        if (path.StartsWith("/v1/"))
        {
            return true;
        }

        return false;
    }

    private string GetSuccessMessage(HttpContext context)
    {
        var method = context.Request.Method;
        var statusCode = context.Response.StatusCode;

        return (method, statusCode) switch
        {
            ("POST", 200) or ("POST", 201) => "Resource created successfully",
            ("PUT", 200) => "Resource updated successfully",
            ("DELETE", 200) or ("DELETE", 204) => "Resource deleted successfully",
            ("GET", 200) => "Success",
            _ => "Success"
        };
    }
}

/// <summary>
/// Extension method to register ApiResponseWrapperMiddleware
/// </summary>
public static class ApiResponseWrapperMiddlewareExtensions
{
    public static IApplicationBuilder UseApiResponseWrapper(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiResponseWrapperMiddleware>();
    }
}
