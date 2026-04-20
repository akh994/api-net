using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkeletonApi.Application.Interfaces;

namespace SkeletonApi.Infrastructure.SSE;

public class SseSubscriptionService : BackgroundService
{
    private readonly ISseManager _sseManager;
    private readonly ILogger<SseSubscriptionService> _logger;

    public SseSubscriptionService(ISseManager sseManager, ILogger<SseSubscriptionService> logger)
    {
        _sseManager = sseManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SseSubscriptionService starting...");

        try
        {
            await _sseManager.SubscribeToRedisAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SseSubscriptionService");
            throw;
        }
    }
}
