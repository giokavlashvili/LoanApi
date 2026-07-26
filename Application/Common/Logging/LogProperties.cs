namespace Application.Common.Logging
{
    /// <summary>
    /// Names of the structured properties attached to log events. These are the contract
    /// between the code that logs and the sinks that read them: today the NLog database
    /// target maps them to columns via <c>${event-properties:item=...}</c>, and a Seq (or
    /// any other structured) target picks the same names up with no code change.
    /// Keep in sync with <c>WebApi/nlog.config</c> and <c>Domain/Entities/Log.cs</c>.
    /// </summary>
    public static class LogProperties
    {
        public const string DurationMs = nameof(DurationMs);
        public const string UserId = nameof(UserId);
        public const string RequestBody = nameof(RequestBody);
        public const string ResponseBody = nameof(ResponseBody);
        public const string Channel = nameof(Channel);

        /// <summary>Values for <see cref="Channel"/>, used to separate high volume
        /// request rows from application errors when querying or purging.</summary>
        public static class Channels
        {
            /// <summary>One row per completed HTTP request/response.</summary>
            public const string Request = "Request";

            /// <summary>Application level events: errors, slow handlers, startup.</summary>
            public const string Api = "Api";
        }
    }
}
