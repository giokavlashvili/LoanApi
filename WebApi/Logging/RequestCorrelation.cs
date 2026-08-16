using Domain.Entities;

namespace WebApi.Logging
{
    /// <summary>
    /// Resolves the per-request correlation id: an inbound <c>X-Correlation-ID</c> when present
    /// and well-formed, otherwise Kestrel's <see cref="HttpContext.TraceIdentifier"/>. The same
    /// value is echoed on the response and stamped onto every log event for that request.
    /// </summary>
    public static class RequestCorrelation
    {
        public const string HeaderName = "X-Correlation-ID";

        private const string ItemKey = "RequestCorrelation.Id";

        public static string Resolve(HttpContext context)
        {
            if (context.Items.TryGetValue(ItemKey, out var cached) && cached is string existing)
                return existing;

            var inbound = context.Request.Headers[HeaderName].FirstOrDefault();
            var raw = IsUsable(inbound) ? inbound!.Trim() : context.TraceIdentifier;
            var value = LogColumnLimits.Truncate(raw, LogColumnLimits.CorrelationId) ?? string.Empty;

            context.Items[ItemKey] = value;
            return value;
        }

        public static void WriteResponseHeader(HttpContext context)
        {
            if (context.Response.HasStarted)
                return;

            if (context.Response.Headers.ContainsKey(HeaderName))
                return;

            context.Response.Headers[HeaderName] = Resolve(context);
        }

        /// <summary>
        /// Rejects empty values and anything with a control character — echoing those back
        /// would be a response-header injection.
        /// </summary>
        private static bool IsUsable(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (var c in value)
            {
                if (char.IsControl(c))
                    return false;
            }

            return true;
        }
    }
}
