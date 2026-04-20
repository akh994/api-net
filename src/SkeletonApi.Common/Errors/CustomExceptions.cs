namespace SkeletonApi.Common.Errors;

/// <summary>
/// Custom exception types for common error scenarios
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
    public ConflictException(string message, Exception innerException) : base(message, innerException) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
    public UnauthorizedException(string message, Exception innerException) : base(message, innerException) { }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
}

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message) { }
    public BadRequestException(string message, Exception innerException) : base(message, innerException) { }
}

public class DataAccessHubException : Exception
{
    public string? Code { get; }
    public DataAccessHubException(string message, string? code = "DATA_ACCESS_ERROR") : base(message) { Code = code; }
    public DataAccessHubException(string message, Exception innerException, string? code = "DATA_ACCESS_ERROR") : base(message, innerException) { Code = code; }
}
