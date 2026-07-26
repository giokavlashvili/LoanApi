namespace Infrastructure.Services
{
    /// <summary>
    /// Bound from the "LogRetention" section of appsettings.json.
    /// </summary>
    public class LogRetentionOptions
    {
        public const string SectionName = "LogRetention";

        public bool Enabled { get; set; } = true;

        /// <summary>Rows older than this are deleted. Set to 0 or less to keep everything.</summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// Rows removed per statement. Batching keeps each delete short so the purge never
        /// holds a long lock on a table the API is actively writing to.
        /// </summary>
        public int BatchSize { get; set; } = 5000;

        public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
    }
}
