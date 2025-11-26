namespace WebApi.Options
{
    /// <summary>
    /// Configuration options for request logging middleware
    /// </summary>
    public class RequestLoggingOptions
    {
        /// <summary>
        /// Maximum body size (in bytes) to log for requests and responses. Default: 100KB (102400 bytes)
        /// </summary>
        public long MaxBodySizeToLog { get; set; } = 102400;

        /// <summary>
        /// Maximum body size (in bytes) for sanitization. Default: 10KB (10240 bytes)
        /// </summary>
        public int MaxBodySizeToSanitize { get; set; } = 10240;

        /// <summary>
        /// Threshold in milliseconds for slow request logging. Default: 1000ms
        /// </summary>
        public long SlowRequestThresholdMs { get; set; } = 1000;

        /// <summary>
        /// HTTP status code threshold for Error log level. Default: 500
        /// </summary>
        public int ErrorStatusCodeThreshold { get; set; } = 500;

        /// <summary>
        /// HTTP status code threshold for Warning log level. Default: 400
        /// </summary>
        public int WarningStatusCodeThreshold { get; set; } = 400;

        /// <summary>
        /// Path patterns to skip logging entirely (e.g., Swagger, static files)
        /// </summary>
        public List<string> SkipLoggingPaths { get; set; } = new()
        {
            "/swagger",
            "/api/specification"
        };

        /// <summary>
        /// File extensions to skip logging (e.g., .js, .css, .png)
        /// </summary>
        public List<string> SkipLoggingExtensions { get; set; } = new()
        {
            ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg",
            ".woff", ".woff2", ".ttf", ".eot", ".map", ".json", ".xml",
            ".pdf", ".zip", ".txt", ".html", ".htm"
        };

        /// <summary>
        /// Path patterns that indicate file download endpoints (don't buffer response)
        /// </summary>
        public List<string> FileDownloadPatterns { get; set; } = new()
        {
            "/download",
            "/file",
            "/video",
            "/audio",
            "/image",
            "/stream",
            "/media",
            "/attachment",
            "/export",
            "/report"
        };

        /// <summary>
        /// Path patterns for file endpoints (don't buffer GET requests to these)
        /// </summary>
        public List<string> FileEndpointPatterns { get; set; } = new()
        {
            "/files/",
            "/videos/",
            "/images/",
            "/documents/",
            "/attachments/"
        };

        /// <summary>
        /// File extensions that indicate binary files (don't buffer response)
        /// </summary>
        public List<string> BinaryFileExtensions { get; set; } = new()
        {
            // Video
            ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv",
            // Audio
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a",
            // Archives
            ".pdf", ".zip", ".rar", ".7z", ".tar", ".gz",
            // Executables
            ".exe", ".dll", ".msi", ".deb", ".rpm",
            // Disk images
            ".iso", ".img", ".bin"
        };
    }
}

