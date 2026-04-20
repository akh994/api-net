using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SkeletonApi.Common.SSE;

/// <summary>
/// Server-Sent Events (SSE) service for real-time updates
/// </summary>
public class ServerSentEventsService
{
    /// <summary>
    /// Send SSE event to client
    /// </summary>
    public static async Task SendEventAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data);
        await SendEventAsync(response, eventName, json, cancellationToken);
    }

    /// <summary>
    /// Send SSE event with string data
    /// </summary>
    public static async Task SendEventAsync(HttpResponse response, string eventName, string data, CancellationToken cancellationToken = default)
    {
        var message = new StringBuilder();
        message.AppendLine($"event: {eventName}");
        message.AppendLine($"data: {data}");
        message.AppendLine();

        await response.WriteAsync(message.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Send SSE comment (for keep-alive)
    /// </summary>
    public static async Task SendCommentAsync(HttpResponse response, string comment = "keep-alive", CancellationToken cancellationToken = default)
    {
        await response.WriteAsync($": {comment}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Configure response for SSE
    /// </summary>
    public static void ConfigureResponse(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Append("Cache-Control", "no-cache");
        response.Headers.Append("Connection", "keep-alive");
        response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering
    }
}

/// <summary>
/// SSE event builder for fluent API
/// </summary>
public class SseEventBuilder
{
    private string? _eventName;
    private string? _data;
    private string? _id;
    private int? _retry;

    public SseEventBuilder WithEvent(string eventName)
    {
        _eventName = eventName;
        return this;
    }

    public SseEventBuilder WithData(string data)
    {
        _data = data;
        return this;
    }

    public SseEventBuilder WithData(object data)
    {
        _data = JsonSerializer.Serialize(data);
        return this;
    }

    public SseEventBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public SseEventBuilder WithRetry(int retryMs)
    {
        _retry = retryMs;
        return this;
    }

    public async Task SendAsync(HttpResponse response, CancellationToken cancellationToken = default)
    {
        var message = new StringBuilder();

        if (!string.IsNullOrEmpty(_id))
            message.AppendLine($"id: {_id}");

        if (!string.IsNullOrEmpty(_eventName))
            message.AppendLine($"event: {_eventName}");

        if (_retry.HasValue)
            message.AppendLine($"retry: {_retry}");

        if (!string.IsNullOrEmpty(_data))
        {
            // Support multi-line data
            foreach (var line in _data.Split('\n'))
            {
                message.AppendLine($"data: {line}");
            }
        }

        message.AppendLine();

        await response.WriteAsync(message.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// SSE stream manager for managing multiple clients
/// </summary>
public class SseStreamManager
{
    private readonly Dictionary<string, List<HttpResponse>> _clients = new();
    private readonly object _lock = new();

    /// <summary>
    /// Add client to stream
    /// </summary>
    public void AddClient(string streamId, HttpResponse response)
    {
        lock (_lock)
        {
            if (!_clients.ContainsKey(streamId))
            {
                _clients[streamId] = new List<HttpResponse>();
            }
            _clients[streamId].Add(response);
        }
    }

    /// <summary>
    /// Remove client from stream
    /// </summary>
    public void RemoveClient(string streamId, HttpResponse response)
    {
        lock (_lock)
        {
            if (_clients.ContainsKey(streamId))
            {
                _clients[streamId].Remove(response);
                if (_clients[streamId].Count == 0)
                {
                    _clients.Remove(streamId);
                }
            }
        }
    }

    /// <summary>
    /// Broadcast event to all clients in stream
    /// </summary>
    public async Task BroadcastAsync(string streamId, string eventName, object data, CancellationToken cancellationToken = default)
    {
        List<HttpResponse> clients;
        lock (_lock)
        {
            if (!_clients.ContainsKey(streamId))
                return;

            clients = new List<HttpResponse>(_clients[streamId]);
        }

        var tasks = clients.Select(client =>
            ServerSentEventsService.SendEventAsync(client, eventName, data, cancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Get client count for stream
    /// </summary>
    public int GetClientCount(string streamId)
    {
        lock (_lock)
        {
            return _clients.ContainsKey(streamId) ? _clients[streamId].Count : 0;
        }
    }
}
