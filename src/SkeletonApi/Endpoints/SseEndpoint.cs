using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SkeletonApi.Application.Interfaces;

namespace SkeletonApi.Endpoints;

[ApiController]
[Route("api/v1/users")]
public class SseEndpoint : ControllerBase
{
    private readonly ISseManager _sseManager;
    private readonly IUserService _userService;
    private readonly ILogger<SseEndpoint> _logger;

    public SseEndpoint(ISseManager sseManager, IUserService userService, ILogger<SseEndpoint> logger)
    {
        _sseManager = sseManager;
        _userService = userService;
        _logger = logger;
    }

    [HttpGet("stream")]
    public async Task StreamUsers(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var clientId = Guid.NewGuid().ToString();
        _logger.LogInformation("SSE connection started for client: {ClientId}", clientId);

        try
        {
            // Send initial connection message
            await SendSseEventAsync(new SseEvent { Type = "connected", Data = new { clientId } });

            // Fetch and send initial user list
            try
            {
                var users = await _userService.GetAllAsync();
                await SendSseEventAsync(new SseEvent { Type = "users", Data = users });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch initial user list for client: {ClientId}", clientId);
                await SendSseEventAsync(new SseEvent { Type = "error", Data = new { message = "Failed to fetch users" } });
            }

            // Subscribe to SSE manager for real-time updates
            await _sseManager.AddClientAsync(clientId, SendSseEventAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE connection closed by client: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for client: {ClientId}", clientId);
        }
    }

    private async Task SendSseEventAsync(SseEvent sseEvent)
    {
        var data = JsonSerializer.Serialize(sseEvent.Data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = $"event: {sseEvent.Type}\ndata: {data}\n\n";
        await Response.WriteAsync(message);
        await Response.Body.FlushAsync();
    }
}
