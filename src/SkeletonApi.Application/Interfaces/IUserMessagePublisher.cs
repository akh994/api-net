using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Application.Interfaces;

/// <summary>
/// Interface for publishing user-related events
/// </summary>
public interface IUserMessagePublisher
{
    /// <summary>
    /// Publish user.created event
    /// </summary>
    Task PublishUserCreatedAsync(User user);

    /// <summary>
    /// Publish user.updated event
    /// </summary>
    Task PublishUserUpdatedAsync(User user);

    /// <summary>
    /// Publish user.deleted event
    /// </summary>
    Task PublishUserDeletedAsync(string userId);
}
