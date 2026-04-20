using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace SkeletonApi.Common.Logging;

/// <summary>
/// Serilog enricher that adds standard .NET Activity TraceId and SpanId to log events.
/// This ensures logs are correlated with the W3C Trace Context used in API responses.
/// </summary>
public class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            // Use standard Activity.Id which follows W3C traceparent format (e.g., 00-traceid-spanid-flags)
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TraceId", activity.Id));

            // Also individual properties if needed for searching
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TraceIdOnly", activity.TraceId.ToHexString()));
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
        }
        else
        {
            // Fallback for cases where no Activity is present
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TraceId", "no-trace"));
        }
    }
}
