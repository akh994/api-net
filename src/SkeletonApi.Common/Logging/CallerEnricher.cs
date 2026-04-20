using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace SkeletonApi.Common.Logging;

/// <summary>
/// Serilog enricher that adds CallerFile and CallerLine properties to the log event.
/// Useful for debugging, but use with caution in high-throughput production as it uses StackTrace.
/// </summary>
public sealed class CallerEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var stackTrace = new StackTrace(true);
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame?.GetMethod();
            var declType = method?.DeclaringType;

            if (declType != null &&
                !declType.FullName!.StartsWith("Serilog") &&
                !declType.FullName!.StartsWith("Microsoft.Extensions.Logging") &&
                !declType.FullName!.StartsWith("Microsoft.AspNetCore") &&
                !declType.FullName!.StartsWith("SkeletonApi.Common.Logging") &&
                !declType.FullName!.StartsWith("System.Runtime"))
            {
                var fileName = frame?.GetFileName();
                if (!string.IsNullOrEmpty(fileName))
                {
                    // Use only the file basename for brevity
                    fileName = System.IO.Path.GetFileName(fileName);
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("CallerFile", fileName));
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("CallerLine", frame!.GetFileLineNumber()));
                    return;
                }
            }
        }
    }
}
