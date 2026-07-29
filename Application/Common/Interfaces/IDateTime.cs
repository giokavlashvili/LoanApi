namespace Application.Common.Interfaces
{
    /// <summary>
    /// The single source of "now" for the whole application. Always UTC.
    /// <para>
    /// Nothing outside <c>DateTimeService</c> may read the system clock directly — a
    /// <c>BannedApiAnalyzers</c> rule makes <see cref="DateTime.Now"/> and friends a build
    /// error. The reason is not testability alone: the previous implementation returned
    /// <em>local</em> time while the Serilog sink and the log retention purge used UTC, so two
    /// tables in one database were timestamped on clocks that differed by the UTC offset.
    /// </para>
    /// </summary>
    public interface IDateTime
    {
        DateTime UtcNow { get; }

        /// <summary>
        /// The server OS's local time. Depends on wherever the process is deployed — a
        /// container with no TZ configured reports this as UTC. For persistence or comparison
        /// use <see cref="UtcNow"/> instead; this is for display only.
        /// </summary>
        DateTime LocalNow { get; }
    }
}
