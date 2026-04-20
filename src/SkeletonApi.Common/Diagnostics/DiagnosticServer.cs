using System.Diagnostics;
using System.Net;
using System.Runtime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SkeletonApi.Common.Diagnostics;

/// <summary>
/// Diagnostic server for profiling and diagnostics
/// Similar to pprof in Go (skeleton-api-go)
/// </summary>
public class DiagnosticServer
{
    private readonly HttpListener _listener;
    private readonly ILogger<DiagnosticServer> _logger;
    private readonly string _addr;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public DiagnosticServer(string host, int port, ILogger<DiagnosticServer> logger)
    {
        _logger = logger;
        _addr = $"{host}:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{host}:{port}/");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting diagnostic server at {Address}", _addr);
        _logger.LogInformation("Diagnostic endpoint: http://{Address}/debug/diagnostics/", _addr);

        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listenerTask = Task.Run(async () => await ListenAsync(_cts.Token), _cts.Token);

        await Task.CompletedTask;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown when listener is stopped
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in diagnostic server");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            var path = request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/":
                case "/debug/diagnostics/":
                    await WriteIndexAsync(response);
                    break;
                case "/debug/diagnostics/gc":
                    await WriteGCInfoAsync(response);
                    break;
                case "/debug/diagnostics/memory":
                    await WriteMemoryInfoAsync(response);
                    break;
                case "/debug/diagnostics/threads":
                    await WriteThreadInfoAsync(response);
                    break;
                case "/debug/diagnostics/process":
                    await WriteProcessInfoAsync(response);
                    break;
                case "/debug/diagnostics/environment":
                    await WriteEnvironmentInfoAsync(response);
                    break;
                default:
                    response.StatusCode = 404;
                    await WriteTextAsync(response, "Not Found");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
        }
    }

    private async Task WriteIndexAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/html";
        var html = @"
<!DOCTYPE html>
<html>
<head><title>Diagnostic Endpoints</title></head>
<body>
<h1>.NET Diagnostic Endpoints</h1>
<p>Similar to pprof in Go</p>
<h2>Available Endpoints:</h2>
<ul>
    <li><a href='/debug/diagnostics/gc'>GC Info</a> - Garbage collection statistics</li>
    <li><a href='/debug/diagnostics/memory'>Memory</a> - Memory usage information</li>
    <li><a href='/debug/diagnostics/threads'>Threads</a> - Thread information</li>
    <li><a href='/debug/diagnostics/process'>Process</a> - Process information</li>
    <li><a href='/debug/diagnostics/environment'>Environment</a> - Environment variables</li>
</ul>
<h2>Profiling Tools:</h2>
<p>Use dotnet-trace for CPU profiling:</p>
<pre>dotnet-trace collect --process-id " + Process.GetCurrentProcess().Id + @" --duration 00:00:30</pre>
<p>Use dotnet-counters for real-time metrics:</p>
<pre>dotnet-counters monitor --process-id " + Process.GetCurrentProcess().Id + @"</pre>
<p>Use dotnet-dump for memory dumps:</p>
<pre>dotnet-dump collect --process-id " + Process.GetCurrentProcess().Id + @"</pre>
</body>
</html>";
        await WriteTextAsync(response, html);
    }

    private async Task WriteGCInfoAsync(HttpListenerResponse response)
    {
        var gcInfo = new
        {
            TotalMemory = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            GCMode = GCSettings.IsServerGC ? "Server" : "Workstation",
            LatencyMode = GCSettings.LatencyMode.ToString(),
            TotalAllocatedBytes = GC.GetTotalAllocatedBytes()
        };
        await WriteJsonAsync(response, gcInfo);
    }

    private async Task WriteMemoryInfoAsync(HttpListenerResponse response)
    {
        var process = Process.GetCurrentProcess();
        var memoryInfo = new
        {
            WorkingSet = process.WorkingSet64,
            PrivateMemory = process.PrivateMemorySize64,
            VirtualMemory = process.VirtualMemorySize64,
            PagedMemory = process.PagedMemorySize64,
            GCTotalMemory = GC.GetTotalMemory(false),
            GCTotalAllocated = GC.GetTotalAllocatedBytes()
        };
        await WriteJsonAsync(response, memoryInfo);
    }

    private async Task WriteThreadInfoAsync(HttpListenerResponse response)
    {
        var process = Process.GetCurrentProcess();
        var threadInfo = new
        {
            ThreadCount = process.Threads.Count,
            ThreadPoolInfo = new
            {
                ThreadCount = ThreadPool.ThreadCount,
                CompletedWorkItemCount = ThreadPool.CompletedWorkItemCount,
                PendingWorkItemCount = ThreadPool.PendingWorkItemCount
            }
        };
        await WriteJsonAsync(response, threadInfo);
    }

    private async Task WriteProcessInfoAsync(HttpListenerResponse response)
    {
        var process = Process.GetCurrentProcess();
        var processInfo = new
        {
            ProcessId = process.Id,
            ProcessName = process.ProcessName,
            StartTime = process.StartTime,
            TotalProcessorTime = process.TotalProcessorTime,
            UserProcessorTime = process.UserProcessorTime,
            PrivilegedProcessorTime = process.PrivilegedProcessorTime,
            HandleCount = process.HandleCount
        };
        await WriteJsonAsync(response, processInfo);
    }

    private async Task WriteEnvironmentInfoAsync(HttpListenerResponse response)
    {
        var envInfo = new
        {
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            OSVersion = Environment.OSVersion.ToString(),
            RuntimeVersion = Environment.Version.ToString(),
            Is64BitProcess = Environment.Is64BitProcess,
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            WorkingDirectory = Environment.CurrentDirectory
        };
        await WriteJsonAsync(response, envInfo);
    }

    private async Task WriteJsonAsync(HttpListenerResponse response, object data)
    {
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await WriteTextAsync(response, json);
    }

    private async Task WriteTextAsync(HttpListenerResponse response, string text)
    {
        var buffer = Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down diagnostic server...");

        try
        {
            // Stop listener first to unblock GetContextAsync
            _listener.Stop();

            // Cancel the token
            _cts?.Cancel();

            // Wait for listener task with timeout
            if (_listenerTask != null)
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var completedTask = await Task.WhenAny(_listenerTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Diagnostic server shutdown timed out after 5 seconds");
                }
            }

            _listener.Close();

            _logger.LogInformation("Diagnostic server stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping diagnostic server");
        }
    }

    public string GetAddress() => _addr;
}
