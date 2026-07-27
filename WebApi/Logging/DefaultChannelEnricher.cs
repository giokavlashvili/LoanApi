using Application.Common.Logging;
using Serilog.Core;
using Serilog.Events;

namespace WebApi.Logging
{
    /// <summary>
    /// Defaults <see cref="LogProperties.Channel"/> to "Api" for every event that does not
    /// set it — the equivalent of NLog's <c>${event-properties:item=Channel:whenEmpty=Api}</c>.
    /// <para>
    /// Enrichers run after the message template is bound, so the default applied here never
    /// overwrites the "Request" channel that <c>LoggingMiddleware</c> sets explicitly.
    /// </para>
    /// </summary>
    public sealed class DefaultChannelEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(LogProperties.Channel, LogProperties.Channels.Api));
        }
    }
}
