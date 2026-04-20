using SkeletonApi.Common.Models;

namespace SkeletonApi.Common.Interfaces;

/// <summary>
/// Interface for accessing current user context and claims
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Get the current user claims
    /// </summary>
    UserClaims GetCurrentUser();

    /// <summary>
    /// Get the current trace ID
    /// </summary>
    string GetTraceId();

    /// <summary>
    /// Check if the current context is authenticated
    /// </summary>
    bool IsAuthenticated();

    /// <summary>
    /// Manually set user claims (used by messaging consumers or background jobs)
    /// </summary>
    void SetUser(UserClaims claims);
}
