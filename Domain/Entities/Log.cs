namespace Domain.Entities
{
    public class Log
    {
        // Primary Key & Timestamp (most queried)
        public long Id { get; set; }
        public DateTime When { get; set; }

        // Severity & Filtering
        public string? Level { get; set; }

        // Request Tracing (for correlation)
        public string? CorrelationId { get; set; }
        public string? RequestId { get; set; }

        // HTTP Context (quick inspection)
        public string? HttpMethod { get; set; }
        public int? StatusCode { get; set; }
        public string? Url { get; set; }

        // Performance Metrics
        public long? DurationMs { get; set; }

        // User Context
        public string? UserId { get; set; }
        public string? UserName { get; set; }

        // Client Context
        public string? ClientIp { get; set; }
        public string? UserAgent { get; set; }

        // Message Content
        public string? Message { get; set; }
        public string? Exception { get; set; }
        public string? Trace { get; set; }

        // Source & Channel
        public string? Logger { get; set; }
        public string? Channel { get; set; }

        // Detailed Data (large fields, inspected less frequently)
        public string? RequestHeaders { get; set; }
        public string? RequestBody { get; set; }
        public string? ResponseBody { get; set; }
        public string? Properties { get; set; }
    }
}
