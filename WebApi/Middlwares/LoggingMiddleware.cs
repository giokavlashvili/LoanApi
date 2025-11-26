using Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Serilog.Context;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WebApi.Helpers;
using WebApi.Options;

namespace WebApi.Middlwares
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly RequestLoggingOptions _options;
        private const string CorrelationIdItemName = "CorrelationId";
        private const string RequestIdItemName = "RequestId";
        private static readonly string[] MethodsWithBody = { "POST", "PUT", "PATCH" };

        public LoggingMiddleware(ILogger<LoggingMiddleware> logger, RequestDelegate next, ICurrentUserService currentUserService, IOptions<RequestLoggingOptions> options)
        {
            _logger = logger;
            _next = next;
            _currentUserService = currentUserService;
            _options = options.Value;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Items[CorrelationIdItemName]?.ToString() ?? Guid.NewGuid().ToString();
            var requestId = Guid.NewGuid().ToString();
            context.Items[RequestIdItemName] = requestId;

            var stopwatch = Stopwatch.StartNew();
            var path = context.Request.Path.Value ?? string.Empty;
            var isRazorPage = path.StartsWith("/Identity", StringComparison.OrdinalIgnoreCase);

            // Capture request context
            var requestContext = CaptureRequestContext(context, isRazorPage);

            // Setup response buffering for API endpoints only
            Stream? originalBodyStream = null;
            MemoryStream? responseBodyStream = null;
            if (!isRazorPage)
            {
                originalBodyStream = context.Response.Body;
                responseBodyStream = new MemoryStream();
                context.Response.Body = responseBodyStream;
            }

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                var durationMs = stopwatch.ElapsedMilliseconds;
                var statusCode = context.Response.StatusCode;

                // Handle response body for API endpoints
                string? responseBody = null;
                if (!isRazorPage && responseBodyStream != null && originalBodyStream != null)
                {
                    // Read body for logging only on errors or slow requests
                    if (statusCode >= _options.WarningStatusCodeThreshold || durationMs > _options.SlowRequestThresholdMs)
                    {
                        // Check response content type and size before reading
                        var responseContentType = context.Response.ContentType?.ToLowerInvariant() ?? string.Empty;
                        var isBinaryResponse = responseContentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                                              responseContentType.Contains("image/", StringComparison.OrdinalIgnoreCase) ||
                                              responseContentType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
                                              responseContentType.Contains("audio/", StringComparison.OrdinalIgnoreCase);

                        if (!isBinaryResponse && responseBodyStream.Length <= _options.MaxBodySizeToLog)
                        {
                            responseBody = await ReadStreamAsync(responseBodyStream);
                            responseBody = LoggingSanitizer.SanitizeBody(responseBody, _options.MaxBodySizeToSanitize);
                        }
                        else if (isBinaryResponse)
                        {
                            responseBody = $"[{responseBodyStream.Length} bytes - {responseContentType}]";
                        }
                        else
                        {
                            responseBody = $"[Response too large: {responseBodyStream.Length} bytes - not logged]";
                        }
                    }

                    // Always copy response back to original stream
                    responseBodyStream.Seek(0, SeekOrigin.Begin);
                    await responseBodyStream.CopyToAsync(originalBodyStream);
                    context.Response.Body = originalBodyStream;
                }

                // Log the request/response
                LogRequest(context, requestContext, statusCode, durationMs, responseBody);
            }
        }

        private RequestContext CaptureRequestContext(HttpContext context, bool isRazorPage)
        {
            var httpMethod = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;
            var queryString = context.Request.QueryString.Value ?? string.Empty;
            var sanitizedQueryString = LoggingSanitizer.SanitizeQueryString(queryString);
            var fullUrl = string.IsNullOrEmpty(sanitizedQueryString) ? path : $"{path}?{sanitizedQueryString}";

            // Capture request body for POST/PUT/PATCH on API endpoints
            string? requestBody = null;
            if (!isRazorPage && MethodsWithBody.Contains(httpMethod, StringComparer.OrdinalIgnoreCase))
            {
                // Skip file uploads and binary content
                var contentType = context.Request.ContentType?.ToLowerInvariant() ?? string.Empty;
                var isFileUpload = contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase);
                var isBinary = contentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                               contentType.Contains("image/", StringComparison.OrdinalIgnoreCase) ||
                               contentType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
                               contentType.Contains("audio/", StringComparison.OrdinalIgnoreCase);

                // Check Content-Length before reading
                var contentLength = context.Request.ContentLength ?? 0;

                if (!isFileUpload && !isBinary && contentLength > 0 && contentLength <= _options.MaxBodySizeToLog)
                {
                    context.Request.EnableBuffering();
                    requestBody = ReadRequestBody(context.Request);
                    requestBody = LoggingSanitizer.SanitizeBody(requestBody, _options.MaxBodySizeToSanitize);
                }
                else if (isFileUpload || isBinary)
                {
                    requestBody = $"[{contentLength} bytes - {contentType}]";
                }
                else if (contentLength > _options.MaxBodySizeToLog)
                {
                    requestBody = $"[Body too large: {contentLength} bytes - not logged]";
                }
            }

            return new RequestContext
            {
                HttpMethod = httpMethod,
                FullUrl = fullUrl,
                ClientIp = GetClientIpAddress(context),
                UserAgent = context.Request.Headers["User-Agent"].ToString(),
                RequestHeaders = JsonSerializer.Serialize(LoggingSanitizer.SanitizeHeaders(context.Request.Headers)),
                RequestBody = requestBody
            };
        }

        private void LogRequest(HttpContext context, RequestContext requestContext, int statusCode, long durationMs, string? responseBody)
        {
            var correlationId = context.Items[CorrelationIdItemName]?.ToString();
            var logLevel = DetermineLogLevel(statusCode, durationMs);
            var message = $"HTTP {requestContext.HttpMethod} {requestContext.FullUrl} responded {statusCode} in {durationMs}ms";

            // Build list of property pushes, only including non-null values
            var propertyPushes = new List<IDisposable>();
            
            if (correlationId != null)
                propertyPushes.Add(LogContext.PushProperty("CorrelationId", correlationId));
            
            if (context.Items[RequestIdItemName] != null)
                propertyPushes.Add(LogContext.PushProperty("RequestId", context.Items[RequestIdItemName]));
            
            if (_currentUserService.UserId != null)
                propertyPushes.Add(LogContext.PushProperty("UserId", _currentUserService.UserId));
            
            if (_currentUserService.Name != null)
                propertyPushes.Add(LogContext.PushProperty("UserName", _currentUserService.Name));
            
            propertyPushes.Add(LogContext.PushProperty("Url", requestContext.FullUrl));
            propertyPushes.Add(LogContext.PushProperty("HttpMethod", requestContext.HttpMethod));
            propertyPushes.Add(LogContext.PushProperty("StatusCode", statusCode));
            propertyPushes.Add(LogContext.PushProperty("DurationMs", durationMs));
            propertyPushes.Add(LogContext.PushProperty("ClientIp", requestContext.ClientIp));
            propertyPushes.Add(LogContext.PushProperty("UserAgent", requestContext.UserAgent));
            propertyPushes.Add(LogContext.PushProperty("RequestHeaders", requestContext.RequestHeaders));
            
            if (requestContext.RequestBody != null)
                propertyPushes.Add(LogContext.PushProperty("RequestBody", requestContext.RequestBody));
            
            if (responseBody != null)
                propertyPushes.Add(LogContext.PushProperty("ResponseBody", responseBody));

            try
            {
                _logger.Log(logLevel, message);
            }
            finally
            {
                // Dispose all property pushes
                foreach (var push in propertyPushes)
                {
                    push.Dispose();
                }
            }
        }

        private static string GetClientIpAddress(HttpContext context)
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var firstIp = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(firstIp))
                    return firstIp.Trim();
            }

            var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
                return realIp;

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        private string? ReadRequestBody(HttpRequest request)
        {
            try
            {
                // Additional safety check
                if (request.ContentLength > _options.MaxBodySizeToLog)
                {
                    return $"[Body too large: {request.ContentLength} bytes - not logged]";
                }

                request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = reader.ReadToEnd();
                request.Body.Position = 0;
                return body;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string?> ReadStreamAsync(Stream stream)
        {
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return null;
            }
        }

        private LogLevel DetermineLogLevel(int statusCode, long durationMs)
        {
            // Error: Server errors
            if (statusCode >= _options.ErrorStatusCodeThreshold)
                return LogLevel.Error;
            
            // Warning: Client errors
            if (statusCode >= _options.WarningStatusCodeThreshold)
                return LogLevel.Warning;
            
            // Information: Successful requests (2xx, 3xx)
            return LogLevel.Information;
        }

        private class RequestContext
        {
            public string HttpMethod { get; set; } = string.Empty;
            public string FullUrl { get; set; } = string.Empty;
            public string ClientIp { get; set; } = string.Empty;
            public string UserAgent { get; set; } = string.Empty;
            public string RequestHeaders { get; set; } = string.Empty;
            public string? RequestBody { get; set; }
        }
    }
}
