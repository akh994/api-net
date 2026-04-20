using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Interfaces;
using SkeletonApi.Common.Messaging;
using SkeletonApi.Common.Messaging.Abstractions;
using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Infrastructure.Messaging;

/// <summary>
/// Message publisher for user events
/// </summary>
public class UserMessagePublisher : SkeletonApi.Application.Interfaces.IUserMessagePublisher
{
    private readonly MessageClientProvider _messageClientProvider;
    private readonly ILogger<UserMessagePublisher> _logger;
    private readonly IUserContext _userContext;

    public UserMessagePublisher(
        ILogger<UserMessagePublisher> logger,
        MessageClientProvider messageClientProvider,
        IUserContext userContext)
    {
        _logger = logger;
        _messageClientProvider = messageClientProvider;
        _userContext = userContext;
    }

    /// <summary>
    /// Publish user.created event
    /// </summary>
    public async Task PublishUserCreatedAsync(User user)
    {
        if (_messageClientProvider == null)
        {
            _logger.LogWarning("Message client not configured, skipping user.created event");
            return;
        }

        try
        {
            var message = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                full_name = user.FullName,
                role = user.Role,
                created_at = user.CreatedAt
            };

            var messageBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            var headers = CreateHeaders("user.created", user.Id ?? string.Empty);

            var client = _messageClientProvider.GetClientForTopic("user.created");
            await client.PublishAsync("user.created", messageBytes, headers);
            _logger.LogInformation("Successfully published user.created event for user: {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish user.created event for user: {UserId}", user.Id);
            // Don't throw - event publishing failure shouldn't fail the request
        }
    }

    /// <summary>
    /// Publish user.updated event
    /// </summary>
    public async Task PublishUserUpdatedAsync(User user)
    {
        if (_messageClientProvider == null)
        {
            _logger.LogWarning("Message client not configured, skipping user.updated event");
            return;
        }

        try
        {
            var message = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                full_name = user.FullName,
                role = user.Role,
                updated_at = user.UpdatedAt
            };

            var messageBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            var headers = CreateHeaders("user.updated", user.Id ?? string.Empty);

            var client = _messageClientProvider.GetClientForTopic("user.updated");
            await client.PublishAsync("user.updated", messageBytes, headers);
            _logger.LogInformation("Successfully published user.updated event for user: {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish user.updated event for user: {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Publish user.deleted event
    /// </summary>
    public async Task PublishUserDeletedAsync(string userId)
    {
        if (_messageClientProvider == null)
        {
            _logger.LogWarning("Message client not configured, skipping user.deleted event");
            return;
        }

        try
        {
            var message = new
            {
                id = userId,
                deleted_at = DateTime.UtcNow
            };

            var messageBytes = JsonSerializer.SerializeToUtf8Bytes(message);
            var headers = CreateHeaders("user.deleted", userId);

            var client = _messageClientProvider.GetClientForTopic("user.deleted");
            await client.PublishAsync("user.deleted", messageBytes, headers);
            _logger.LogInformation("Successfully published user.deleted event for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish user.deleted event for user: {UserId}", userId);
        }
    }

    private Dictionary<string, string> CreateHeaders(string eventType, string userId)
    {
        var headers = new Dictionary<string, string>
        {
            ["event_type"] = eventType,
            ["user_id"] = userId,
            ["timestamp"] = DateTime.UtcNow.ToString("o")
        };

        var claims = _userContext.GetCurrentUser();
        if (!string.IsNullOrEmpty(claims.Id)) headers["x-user-id"] = claims.Id;
        if (!string.IsNullOrEmpty(claims.Username)) headers["x-user-name"] = claims.Username;
        if (!string.IsNullOrEmpty(claims.Email)) headers["x-user-email"] = claims.Email;
        if (!string.IsNullOrEmpty(claims.Role)) headers["x-user-role"] = claims.Role;

        var traceId = _userContext.GetTraceId();
        if (!string.IsNullOrEmpty(traceId)) headers["traceparent"] = traceId;

        _logger.LogDebug("Publisher: Attaching claims to message headers. UserId: {UserId}, Username: {Username}, Role: {Role}",
            claims.Id, claims.Username, claims.Role);

        return headers;
    }
}
