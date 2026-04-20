using Serilog.Core;
using Serilog.Events;

namespace SkeletonApi.Common.Logging;

/// <summary>
/// Serilog enricher that adds a <c>UtcTimestamp</c> property formatted in UTC (ISO 8601),
/// regardless of the server's local timezone. Use <c>{UtcTimestamp}</c> in your output template.
/// </summary>
public sealed class UtcTimestampEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("UtcTimestamp", logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")));
}
