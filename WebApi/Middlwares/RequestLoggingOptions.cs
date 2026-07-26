using Application.Common.Logging;

namespace WebApi.Middlwares
{
    /// <summary>
    /// Bound from the "RequestLogging" section of appsettings.json.
    /// </summary>
    public class RequestLoggingOptions
    {
        public const string SectionName = "RequestLogging";

        public bool Enabled { get; set; } = true;

        public bool LogRequestBody { get; set; } = true;

        public bool LogResponseBody { get; set; } = true;

        /// <summary>
        /// Hard cap per body. Anything larger is truncated with a marker rather than stored,
        /// which is what keeps a single oversized payload from bloating the Logs table.
        /// </summary>
        public int MaxBodyBytes { get; set; } = 32 * 1024;

        /// <summary>
        /// Allowlist — a body is only ever read when its content type is listed here.
        /// Anything else (multipart uploads, octet-stream, images, PDFs, archives) is
        /// summarised by type and length and its content is never touched.
        /// Media types ending in "+json" are always treated as loggable.
        /// </summary>
        public string[] LoggableContentTypes { get; set; } =
        {
            "application/json",
            "application/problem+json",
            "application/xml",
            "text/xml",
            "text/plain",
            "application/x-www-form-urlencoded"
        };

        /// <summary>Property names masked in captured bodies. Empty falls back to <see cref="LogRedactor.DefaultSensitiveProperties"/>.</summary>
        public string[] SensitiveProperties { get; set; } = Array.Empty<string>();

        /// <summary>Request path prefixes that produce no log row at all (case-insensitive).</summary>
        public string[] IgnoredPaths { get; set; } =
        {
            "/health",
            "/swagger",
            "/favicon.ico",
            "/_framework"
        };

        /// <summary>
        /// Response status codes at or above this are logged as Warning so they reach the
        /// database target even if request logging is later routed elsewhere.
        /// </summary>
        public int WarningStatusCodeThreshold { get; set; } = 400;
    }
}
