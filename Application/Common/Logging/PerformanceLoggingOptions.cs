namespace Application.Common.Logging
{
    /// <summary>
    /// Bound from the "PerformanceLogging" section of appsettings.json.
    /// </summary>
    public class PerformanceLoggingOptions
    {
        public const string SectionName = "PerformanceLogging";

        /// <summary>
        /// Handlers slower than this produce a Warning (so they reach the Logs table).
        /// </summary>
        public int LongRunningThresholdMs { get; set; } = 500;
    }
}
