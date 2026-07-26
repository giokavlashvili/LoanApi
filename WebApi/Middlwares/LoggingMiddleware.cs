using Application.Common.Interfaces;
using Application.Common.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace WebApi.Middlwares
{
    /// <summary>
    /// Writes exactly one structured row per HTTP request, carrying everything needed to
    /// troubleshoot it: correlation id, method, url, status, duration, user, client ip and
    /// the (redacted, size capped) request and response bodies.
    /// <para>
    /// Small scalars travel as message-template properties and the two bodies as scope
    /// properties, so the rendered <c>Message</c> stays short while every field is still a
    /// separate column. Both are plain NLog event/scope properties, so pointing a Seq
    /// target at this later needs no code change.
    /// </para>
    /// </summary>
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly RequestLoggingOptions _options;

        public LoggingMiddleware(
            RequestDelegate next,
            ILogger<LoggingMiddleware> logger,
            ICurrentUserService currentUserService,
            IOptions<RequestLoggingOptions> options)
        {
            _next = next;
            _logger = logger;
            _currentUserService = currentUserService;
            _options = options.Value;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!_options.Enabled || IsIgnoredPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;

            var requestBody = await CaptureRequestBodyAsync(context.Request);

            var originalResponseBody = context.Response.Body;
            var bufferedResponse = new BoundedResponseBufferStream(
                originalResponseBody,
                context.Response,
                IsLoggableContentType,
                _options.MaxBodyBytes);

            if (_options.LogResponseBody)
                context.Response.Body = bufferedResponse;

            var timer = Stopwatch.StartNew();
            Exception? failure = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Recorded, then rethrown: this middleware observes, it does not handle.
                // Without this the finally below would still run, but the row would lose
                // the exception detail that makes a failed request diagnosable.
                failure = ex;
                throw;
            }
            finally
            {
                timer.Stop();

                // Restore before logging so nothing downstream writes into the wrapper.
                if (_options.LogResponseBody)
                    context.Response.Body = originalResponseBody;

                var responseBody = ResolveResponseBody(context, bufferedResponse);

                WriteLog(context, method, path, timer.ElapsedMilliseconds, requestBody, responseBody, failure);

                bufferedResponse.Dispose();
            }
        }

        private string? ResolveResponseBody(HttpContext context, BoundedResponseBufferStream bufferedResponse)
        {
            if (!_options.LogResponseBody)
                return null;

            // A partial document cannot be parsed, so it cannot be masked — dropping it is
            // the only safe option, and the marker records that something was there.
            if (bufferedResponse.IsTruncated)
                return $"[response body omitted: exceeded {_options.MaxBodyBytes} byte cap]";

            var captured = bufferedResponse.GetCapturedText();

            if (captured is null)
                return bufferedResponse.SkipReason;

            return LogRedactor.Redact(captured, GetMediaType(context.Response.ContentType), _options.SensitiveProperties);
        }

        private void WriteLog(
            HttpContext context,
            string method,
            string path,
            long elapsedMs,
            string? requestBody,
            string? responseBody,
            Exception? failure)
        {
            // Read the user only now: authentication runs downstream of this middleware, so
            // the claims principal is not populated on the way in.
            var userId = _currentUserService.UserId ?? string.Empty;

            var statusCode = failure is not null && !context.Response.HasStarted
                ? StatusCodes.Status500InternalServerError
                : context.Response.StatusCode;

            var level = failure is not null || statusCode >= _options.WarningStatusCodeThreshold
                ? LogLevel.Warning
                : LogLevel.Information;

            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                [LogProperties.RequestBody] = requestBody,
                [LogProperties.ResponseBody] = responseBody
            }))
            {
                _logger.Log(
                    level,
                    failure,
                    "HTTP {Method} {Path} responded {StatusCode} in {DurationMs} ms [{Channel}] user={UserId}",
                    method,
                    path,
                    statusCode,
                    elapsedMs,
                    LogProperties.Channels.Request,
                    userId);
            }
        }

        private async Task<string?> CaptureRequestBodyAsync(HttpRequest request)
        {
            if (!_options.LogRequestBody)
                return null;

            var contentLength = request.ContentLength;

            if (contentLength is null or 0)
                return null;

            if (!IsLoggableContentType(request.ContentType))
            {
                // Uploads land here: record what arrived, never what was in it.
                return $"[request body omitted: {request.ContentType ?? "unknown content type"}, {contentLength} bytes]";
            }

            if (contentLength > _options.MaxBodyBytes)
                return $"[request body omitted: {contentLength} bytes exceeds {_options.MaxBodyBytes} byte cap]";

            try
            {
                // Required so model binding can read the stream again after we rewind it.
                request.EnableBuffering();

                using var reader = new StreamReader(
                    request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                return LogRedactor.Redact(body, GetMediaType(request.ContentType), _options.SensitiveProperties);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return "[request body unavailable]";
            }
        }

        private bool IsLoggableContentType(string? contentType)
        {
            var mediaType = GetMediaType(contentType);

            if (mediaType is null)
                return false;

            // Any structured JSON flavour is safe to read (application/merge-patch+json, ...).
            if (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                return true;

            return _options.LoggableContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Strips parameters, so "application/json; charset=utf-8" matches "application/json".</summary>
        private static string? GetMediaType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return null;

            var separator = contentType.IndexOf(';');
            return (separator >= 0 ? contentType[..separator] : contentType).Trim();
        }

        private bool IsIgnoredPath(PathString path)
        {
            if (!path.HasValue)
                return false;

            foreach (var ignored in _options.IgnoredPaths)
            {
                if (path.StartsWithSegments(ignored, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
