using System.Text.Json.Serialization;

namespace SkeletonApi.Common.Http.Response;

/// <summary>
/// Standard REST API response wrapper
/// </summary>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates if the request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Response data
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    /// <summary>
    /// Error details (if any)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Errors { get; set; }

    /// <summary>
    /// Error code for categorization
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    /// <summary>
    /// Trace ID for debugging
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }

    /// <summary>
    /// Response timestamp in ISO 8601 format
    /// </summary>
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
}

/// <summary>
/// Non-generic API response for cases where no data is returned
/// </summary>
public class ApiResponse : ApiResponse<object>
{
}

/// <summary>
/// Pagination metadata
/// </summary>
public class PaginationMeta
{
    /// <summary>
    /// Current page number
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Total number of items
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }
}

/// <summary>
/// Paginated response data
/// </summary>
public class PaginatedData<T>
{
    /// <summary>
    /// List of items
    /// </summary>
    public IEnumerable<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>
    /// Pagination metadata
    /// </summary>
    public PaginationMeta? Pagination { get; set; }
}

/// <summary>
/// Helper class for creating standardized API responses
/// </summary>
public static class ApiResponseFactory
{
    /// <summary>
    /// Create a success response with data
    /// </summary>
    public static ApiResponse<T> Success<T>(T data, string message = "Success", string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a success response without data
    /// </summary>
    public static ApiResponse Success(string message = "Success", string? traceId = null)
    {
        return new ApiResponse
        {
            Success = true,
            Message = message,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create an error response
    /// </summary>
    public static ApiResponse<T> Error<T>(string message, string? code = null, object? errors = null, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Code = code,
            Errors = errors,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create an error response without data type
    /// </summary>
    public static ApiResponse Error(string message, string? code = null, object? errors = null, string? traceId = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = code,
            Errors = errors,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a paginated success response
    /// </summary>
    public static ApiResponse<PaginatedData<T>> Paginated<T>(
        IEnumerable<T> items,
        int page,
        int limit,
        int totalItems,
        string message = "Success",
        string? traceId = null)
    {
        var totalPages = (int)Math.Ceiling((double)totalItems / limit);

        return new ApiResponse<PaginatedData<T>>
        {
            Success = true,
            Message = message,
            TraceId = traceId,
            Data = new PaginatedData<T>
            {
                Items = items,
                Pagination = new PaginationMeta
                {
                    Page = page,
                    Limit = limit,
                    TotalItems = totalItems,
                    TotalPages = totalPages
                }
            },
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a validation error response
    /// </summary>
    public static ApiResponse ValidationError(Dictionary<string, string[]> validationErrors, string message = "Validation failed")
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "VALIDATION_ERROR",
            Errors = validationErrors,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a not found error response
    /// </summary>
    public static ApiResponse NotFound(string message = "Resource not found", string? resourceId = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "NOT_FOUND",
            Errors = resourceId != null ? new { ResourceId = resourceId } : null,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create an unauthorized error response
    /// </summary>
    public static ApiResponse Unauthorized(string message = "Unauthorized access")
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "UNAUTHORIZED",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a forbidden error response
    /// </summary>
    public static ApiResponse Forbidden(string message = "Access forbidden")
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "FORBIDDEN",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create a bad request error response
    /// </summary>
    public static ApiResponse BadRequest(string message = "Bad request", object? errors = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "BAD_REQUEST",
            Errors = errors,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// Create an internal server error response
    /// </summary>
    public static ApiResponse InternalServerError(string message = "Internal server error", string? traceId = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Code = "INTERNAL_SERVER_ERROR",
            TraceId = traceId,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }
}
