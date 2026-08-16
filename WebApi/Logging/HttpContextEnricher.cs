using Domain.Entities;
using Microsoft.AspNetCore.Http.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace WebApi.Logging
{
    /// <summary>
    /// Stamps request context onto every event raised while an HTTP request is in flight,
    /// so a warning logged deep inside a handler still carries the correlation id, url and
    /// caller that produced it.
    /// <para>
    /// This replaces NLog's <c>${aspnet-*}</c> layout renderers, which read HttpContext at
    /// render time. Serilog has no ambient equivalent — context has to be attached to the
    /// event as it is created, which is what an enricher is for.
    /// </para>
    /// Values are truncated to <see cref="LogColumnLimits"/> so one oversized URL cannot
    /// fail a whole SQL Server batch.
    /// </summary>
    public sealed class HttpContextEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextEnricher(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var context = _httpContextAccessor.HttpContext;

            // Null outside a request: startup, the retention background service, shutdown.
            if (context is null)
                return;

            AddIfAbsent(logEvent, propertyFactory, nameof(Domain.Entities.Log.CorrelationId), RequestCorrelation.Resolve(context));
            AddIfAbsent(logEvent, propertyFactory, nameof(Domain.Entities.Log.Method),
                LogColumnLimits.Truncate(context.Request.Method, LogColumnLimits.Method));
            AddIfAbsent(logEvent, propertyFactory, nameof(Domain.Entities.Log.Url),
                LogColumnLimits.Truncate(context.Request.GetDisplayUrl(), LogColumnLimits.Url));
            AddIfAbsent(logEvent, propertyFactory, nameof(Domain.Entities.Log.ClientIp),
                LogColumnLimits.Truncate(context.Connection.RemoteIpAddress?.ToString(), LogColumnLimits.ClientIp));
            AddIfAbsent(logEvent, propertyFactory, nameof(Domain.Entities.Log.UserName),
                LogColumnLimits.Truncate(context.User?.Identity?.Name, LogColumnLimits.UserName));
        }

        /// <summary>
        /// AddPropertyIfAbsent, never AddOrUpdate: a caller that puts one of these names in
        /// its own message template meant that value, and the ambient one must not clobber it.
        /// </summary>
        private static void AddIfAbsent(
            LogEvent logEvent,
            ILogEventPropertyFactory propertyFactory,
            string name,
            string? value)
        {
            if (!string.IsNullOrEmpty(value))
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
        }
    }
}
