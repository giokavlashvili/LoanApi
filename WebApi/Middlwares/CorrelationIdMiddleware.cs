namespace WebApi.Middlwares
{
    /// <summary>
    /// Settles the correlation id for the request and echoes it back to the caller.
    /// <para>
    /// Two gaps this closes. First, <c>HttpContext.TraceIdentifier</c> is generated per
    /// connection-request pair and means nothing outside this process, so a caller that
    /// already has an id — a gateway, a batch job, a front end retrying — could not thread it
    /// through, and the same logical operation landed in the <c>Logs</c> table under two
    /// unrelated ids. An inbound <c>X-Correlation-ID</c> now wins.
    /// </para>
    /// <para>
    /// Second, the id was never returned. A user reporting "I got an error at about 3pm" had
    /// nothing to quote and support was reduced to searching by timestamp. The response now
    /// carries the header on every request, including failures.
    /// </para>
    /// <para>
    /// Registered <strong>outside</strong> <c>LoggingMiddleware</c> so the id is settled before
    /// the first row is written, and deliberately not subject to
    /// <c>RequestLogging:Enabled</c> — turning request logging off should not also strip a
    /// header clients may depend on.
    /// </para>
    /// </summary>
    public class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-ID";

        /// <summary>
        /// Long enough for a GUID or a W3C trace id, short enough that a hostile caller cannot
        /// use the header to write an unbounded string into the Logs table. Matches the
        /// nvarchar(64) that <c>LogConfiguration</c> gives the column — a longer value would
        /// fail the insert, and the sink reports that only to SelfLog.
        /// </summary>
        private const int MaxLength = 64;

        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = ResolveCorrelationId(context);

            // Assigning TraceIdentifier rather than stashing the value in Items is what makes
            // this reach everything: HttpContextEnricher, the exception middleware and any
            // handler already read it, so none of them need to know this middleware exists.
            context.TraceIdentifier = correlationId;

            // OnStarting, not a plain assignment: headers cannot be added once the response has
            // begun, and something downstream may start writing before this returns.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            await _next(context);
        }

        /// <summary>
        /// An inbound id is used only if it is safe to store and safe to echo. Anything else
        /// falls back to the framework's own identifier rather than being rejected — a bad
        /// header is not worth failing a request over.
        /// </summary>
        private static string ResolveCorrelationId(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
                return context.TraceIdentifier;

            var candidate = values.ToString();

            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
                return context.TraceIdentifier;

            // The value is written to a response header and to a log row. Restricting it to
            // printable ASCII without separators keeps a caller from injecting CR/LF into the
            // response or control characters into the log.
            foreach (var character in candidate)
            {
                var acceptable = char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';

                if (!acceptable)
                    return context.TraceIdentifier;
            }

            return candidate;
        }
    }
}
