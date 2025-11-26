using Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog.Context;
using System.Net;

namespace WebApi.Middlwares
{
    public class UnhandledExceptionHandlerMiddlware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<UnhandledExceptionHandlerMiddlware> _logger;
        private readonly ICurrentUserService _currentUserService;
        private const string CorrelationIdItemName = "CorrelationId";
        private const string RequestIdItemName = "RequestId";

        public UnhandledExceptionHandlerMiddlware(RequestDelegate next, ICurrentUserService currentUserService, IDateTime dateTimeService, ILogger<UnhandledExceptionHandlerMiddlware> logger)
        {
            _next = next;
            _logger = logger;
            _currentUserService = currentUserService;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            // Log unhandled exceptions and modify response to avoid sensitive data exposure
            catch (Exception ex)
            {
                var correlationId = context.Items[CorrelationIdItemName]?.ToString();
                var requestId = context.Items[RequestIdItemName]?.ToString();
                var httpMethod = context.Request.Method;
                var path = context.Request.Path.Value ?? string.Empty;
                var clientIp = GetClientIpAddress(context);
                var userAgent = context.Request.Headers["User-Agent"].ToString();

                // Enrich LogContext with all available context
                using (LogContext.PushProperty("CorrelationId", correlationId))
                using (LogContext.PushProperty("RequestId", requestId))
                using (LogContext.PushProperty("StatusCode", (int)HttpStatusCode.InternalServerError))
                using (LogContext.PushProperty("UserId", _currentUserService.UserId))
                using (LogContext.PushProperty("UserName", _currentUserService.Name))
                using (LogContext.PushProperty("HttpMethod", httpMethod))
                using (LogContext.PushProperty("ClientIp", clientIp))
                using (LogContext.PushProperty("UserAgent", userAgent))
                {
                    _logger.LogError(ex, "Api Request: Unhandled Exception for Request {Path}", path);
                }

                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                var result = new ProblemDetails { 
                    Status = (int)HttpStatusCode.InternalServerError, 
                    Title = "Internal Server Error",
                    Type = "https://www.rfc-editor.org/rfc/rfc7231#section-6.6.1",
                    Detail = "Something went wrong! Contact support."
                };
                var dtoJson = JsonConvert.SerializeObject(result);
                await context.Response.WriteAsync(dtoJson);
            }
        }

        private static string GetClientIpAddress(HttpContext context)
        {
            // Check for forwarded IP (when behind proxy/load balancer)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0)
                {
                    return ips[0].Trim();
                }
            }

            var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}
