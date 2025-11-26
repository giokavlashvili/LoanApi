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
            var path = context.Request.Path.Value ?? string.Empty;

            // Skip logging for problematic routes (always skip Swagger as fallback)
            if (ShouldSkipLogging(path))
            {
                await _next(context);
                return;
            }

            var correlationId = context.Items[CorrelationIdItemName]?.ToString() ?? Guid.NewGuid().ToString();
            var requestId = Guid.NewGuid().ToString();
            context.Items[RequestIdItemName] = requestId;

            var stopwatch = Stopwatch.StartNew();
            var isRazorPage = path.StartsWith("/Identity", StringComparison.OrdinalIgnoreCase);

            // Capture request context
            RequestContext? requestContext = null;
            try
            {
                requestContext = CaptureRequestContext(context, isRazorPage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture request context for {Path}", path);
                // Continue without request context
            }

            // Check if we should buffer response (skip for binary/streaming content)
            var shouldBufferResponse = !isRazorPage && ShouldBufferResponse(context);

            // Setup response buffering for API endpoints only
            Stream? originalBodyStream = null;
            MemoryStream? responseBodyStream = null;
            bool responseBuffered = false;

            if (shouldBufferResponse)
            {
                try
                {
                    if (!context.Response.HasStarted)
                    {
                        originalBodyStream = context.Response.Body;
                        responseBodyStream = new MemoryStream();
                        context.Response.Body = responseBodyStream;
                        responseBuffered = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to buffer response for {Path}", path);
                    // Continue without response buffering
                }
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
                var currentPath = context.Request.Path.Value ?? string.Empty;
                
                if (responseBuffered && responseBodyStream != null && originalBodyStream != null)
                {
                    try
                    {
                        // Read body for logging only on errors or slow requests
                        if (statusCode >= _options.WarningStatusCodeThreshold || durationMs > _options.SlowRequestThresholdMs)
                        {
                            // Check response content type and size before reading
                            var responseContentType = context.Response.ContentType?.ToLowerInvariant() ?? string.Empty;
                            var isBinaryResponse = IsBinaryContentType(responseContentType);

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

                        // Copy response back to original stream (only if response hasn't started)
                        if (!context.Response.HasStarted)
                        {
                            // Check if response is a large binary file - if so, log warning
                            // This is a safety check in case we buffered something we shouldn't have
                            var responseContentType = context.Response.ContentType?.ToLowerInvariant() ?? string.Empty;
                            var isLargeBinaryFile = IsBinaryContentType(responseContentType) && 
                                                   responseBodyStream.Length > _options.MaxBodySizeToLog;

                            if (isLargeBinaryFile)
                            {
                                // Large binary file was buffered - log warning
                                _logger.LogWarning(
                                    "Large binary file ({Size} bytes, {ContentType}) was buffered for {Path}. " +
                                    "Consider excluding this endpoint from buffering.",
                                    responseBodyStream.Length, responseContentType, currentPath);
                            }

                            try
                            {
                                responseBodyStream.Seek(0, SeekOrigin.Begin);
                                await responseBodyStream.CopyToAsync(originalBodyStream);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to copy response body to original stream for {Path}", currentPath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process response body for {Path}", currentPath);
                    }
                    finally
                    {
                        // Always restore original stream
                        try
                        {
                            context.Response.Body = originalBodyStream;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to restore original response stream for {Path}", path);
                        }
                    }
                }

                // Log the request/response
                if (requestContext != null)
                {
                    try
                    {
                        LogRequest(context, requestContext, statusCode, durationMs, responseBody);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to log request for {Path}", path);
                    }
                }
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

                // Check if stream supports seeking
                if (!request.Body.CanSeek)
                {
                    return null;
                }

                request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = reader.ReadToEnd();
                request.Body.Position = 0;
                return body;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read request body");
                return null;
            }
        }

        private static async Task<string?> ReadStreamAsync(Stream stream)
        {
            try
            {
                if (!stream.CanSeek)
                {
                    return null;
                }

                stream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines if logging should be skipped for the given path
        /// </summary>
        private bool ShouldSkipLogging(string path)
        {
            var lowerPath = path.ToLowerInvariant();

            // Check path patterns
            if (_options.SkipLoggingPaths.Any(pattern => 
                lowerPath.StartsWith(pattern.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check file extensions
            if (_options.SkipLoggingExtensions.Any(ext => 
                lowerPath.EndsWith(ext.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if response should be buffered based on content type and path
        /// </summary>
        private bool ShouldBufferResponse(HttpContext context)
        {
            var requestContentType = context.Request.ContentType?.ToLowerInvariant() ?? string.Empty;
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var httpMethod = context.Request.Method.ToUpperInvariant();
            
            // Don't buffer if request is binary/streaming
            if (IsBinaryContentType(requestContentType) || 
                requestContentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Don't buffer file download endpoints (from configuration)
            if (_options.FileDownloadPatterns.Any(pattern => 
                path.Contains(pattern.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;  // Don't buffer file downloads
            }

            // Don't buffer GET requests to paths that look like file endpoints
            // (e.g., /api/files/123, /api/videos/456)
            if (httpMethod == "GET" && 
                _options.FileEndpointPatterns.Any(pattern => 
                    path.Contains(pattern.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Check for file extensions in path (e.g., /api/files/video.mp4)
            if (_options.BinaryFileExtensions.Any(ext => 
                path.EndsWith(ext.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;  // Don't buffer files with these extensions
            }

            return true;
        }

        /// <summary>
        /// Determines if content type is binary/streaming
        /// </summary>
        private static bool IsBinaryContentType(string contentType)
        {
            return contentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("image/", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("audio/", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("application/zip", StringComparison.OrdinalIgnoreCase);
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
