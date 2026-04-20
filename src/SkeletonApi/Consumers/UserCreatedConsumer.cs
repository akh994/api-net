using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Messaging;
using SkeletonApi.Common.Messaging.Abstractions;

namespace SkeletonApi.Consumers;

/// <summary>
/// Background service that consumes user.created events
/// </summary>
public class UserCreatedConsumer : BaseMessageConsumer<UserCreatedMessage>
{
    public UserCreatedConsumer(
        IServiceProvider serviceProvider,
        IOptions<MessageConsumersOptions> options,
        MessageClientFactory clientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<UserCreatedConsumer> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
        : base(serviceProvider, options, clientFactory, scopeFactory, logger, configuration)
    {
    }

    protected override string Topic => "user.created";

    protected override async Task ProcessMessageAsync(UserCreatedMessage message, IServiceScope scope, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Processing user.created event for user: {UserId}, Username: {Username}",
            message.Id,
            message.Username);

        // Use provided scope which has UserContext populated
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        // Process the user created event via UserService
        await userService.ProcessUserCreatedAsync(message.Id);

        Logger.LogInformation(
            "Successfully processed user.created event for user: {UserId}",
            message.Id);
    }
}

/// <summary>
/// Message structure for user.created events
/// </summary>
public class UserCreatedMessage
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Role { get; set; }
    public DateTime? CreatedAt { get; set; }
}
