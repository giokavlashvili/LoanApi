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
    }
}

