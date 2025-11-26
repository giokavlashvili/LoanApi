using Serilog.Context;

namespace WebApi.Middlwares
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CorrelationIdHeaderName = "X-Correlation-ID";
        private const string CorrelationIdItemName = "CorrelationId";

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            // Try to get correlation ID from request header, otherwise generate a new one
            var correlationId = context.Request.Headers[CorrelationIdHeaderName].FirstOrDefault() 
                ?? Guid.NewGuid().ToString();

            // Store in HttpContext.Items for use in other middleware/services
            context.Items[CorrelationIdItemName] = correlationId;

            // Add to response header so clients can track requests
            context.Response.Headers[CorrelationIdHeaderName] = correlationId;

            // Enrich Serilog context with correlation ID
            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await _next(context);
            }
        }
    }
}


