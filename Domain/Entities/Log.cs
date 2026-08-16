namespace Domain.Entities
{
    /// <summary>
    /// A log row written by the Serilog SQL Server sink. Nothing in the application writes
    /// this through EF — the DbSet exists so logs can be queried. Column shape is configured
    /// in <c>Infrastructure/Persistence/Configurations/LogConfiguration.cs</c> and must stay
    /// in sync with the column mapping in <c>WebApi/Extensions/LoggingConfiguration.cs</c>.
    /// </summary>
    public class Log
    {
        public long Id { get; set; }

        /// <summary>Event time in UTC (not insert time — the target is async and batched).</summary>
        public DateTime When { get; set; }

        public string? Level { get; set; }

        public string? Logger { get; set; }

        public string? Message { get; set; }

        public string? Exception { get; set; }

        /// <summary>
        /// Ties every row produced by one HTTP request together: the request/response row,
        /// any slow-handler warning, and any exception. An inbound <c>X-Correlation-ID</c>
        /// when present, otherwise the ASP.NET trace identifier.
        /// </summary>
        public string? CorrelationId { get; set; }

        public string? Method { get; set; }

        public string? Url { get; set; }

        public int? StatusCode { get; set; }

        public int? DurationMs { get; set; }

        public string? UserId { get; set; }

        public string? UserName { get; set; }

        public string? ClientIp { get; set; }

        /// <summary>Which instance emitted the row, once the API runs behind a load balancer.</summary>
        public string? MachineName { get; set; }

        /// <summary>Redacted request body. Null for file uploads and other non-textual payloads.</summary>
        public string? RequestBody { get; set; }

        /// <summary>Redacted response body. Null for downloads and other non-textual payloads.</summary>
        public string? ResponseBody { get; set; }

        /// <summary>"Request" for request/response rows, "Api" for application events.</summary>
        public string? Channel { get; set; }
    }
}
