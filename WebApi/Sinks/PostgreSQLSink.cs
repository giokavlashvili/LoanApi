using Npgsql;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace WebApi.Sinks
{
    public class PostgreSQLSink : ILogEventSink
    {
        private readonly string _connectionString;
        private readonly IFormatProvider? _formatProvider;
        private readonly JsonFormatter _jsonFormatter;

        public PostgreSQLSink(string connectionString, IFormatProvider? formatProvider = null)
        {
            _connectionString = connectionString;
            _formatProvider = formatProvider;
            _jsonFormatter = new JsonFormatter();
        }

        public void Emit(LogEvent logEvent)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();

                var sql = @"INSERT INTO ""Logs"" (""When"", ""Level"", ""CorrelationId"", ""RequestId"", ""HttpMethod"", ""StatusCode"", ""Url"", ""DurationMs"", ""UserId"", ""UserName"", ""ClientIp"", ""UserAgent"", ""Message"", ""Exception"", ""Trace"", ""Logger"", ""Channel"", ""RequestHeaders"", ""RequestBody"", ""ResponseBody"", ""Properties"") 
                           VALUES (@when, @level, @correlationId, @requestId, @httpMethod, @statusCode, @url, @durationMs, @userId, @userName, @clientIp, @userAgent, @message, @exception, @trace, @logger, @channel, @requestHeaders, @requestBody, @responseBody, @properties)";

                using var command = new NpgsqlCommand(sql, connection);

                // Extract properties from LogContext
                var correlationId = GetPropertyValue(logEvent, "CorrelationId");
                var requestId = GetPropertyValue(logEvent, "RequestId");
                var httpMethod = GetPropertyValue(logEvent, "HttpMethod");
                var statusCode = GetIntPropertyValue(logEvent, "StatusCode");
                var durationMs = GetLongPropertyValue(logEvent, "DurationMs");
                var userId = GetPropertyValue(logEvent, "UserId");
                var userName = GetPropertyValue(logEvent, "UserName");
                var clientIp = GetPropertyValue(logEvent, "ClientIp");
                var userAgent = GetPropertyValue(logEvent, "UserAgent");
                var requestHeaders = GetPropertyValue(logEvent, "RequestHeaders");
                var requestBody = GetPropertyValue(logEvent, "RequestBody");
                var responseBody = GetPropertyValue(logEvent, "ResponseBody");

                // Extract URL from properties if available
                var url = GetPropertyValue(logEvent, "Url") 
                    ?? GetPropertyValue(logEvent, "RequestPath")
                    ?? string.Empty;

                // Extract Logger name from SourceContext
                var logger = GetPropertyValue(logEvent, "SourceContext") ?? string.Empty;

                // Format message
                var message = logEvent.RenderMessage(_formatProvider);

                // Get exception details
                var exception = logEvent.Exception != null
                    ? logEvent.Exception.ToString()
                    : string.Empty;

                // Get stack trace
                var trace = logEvent.Exception?.StackTrace ?? string.Empty;

                // Get level as string (abbreviated to fit VARCHAR(15) constraint)
                var level = GetLevelAbbreviation(logEvent.Level);

                // Build Properties JSON from remaining properties (excluding those already extracted)
                var excludedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "CorrelationId", "RequestId", "HttpMethod", "StatusCode", "DurationMs",
                    "UserId", "UserName", "ClientIp", "UserAgent", "RequestHeaders",
                    "RequestBody", "ResponseBody", "Url", "RequestPath", "SourceContext",
                    "MessageTemplate", "Timestamp"
                };

                var additionalProperties = new Dictionary<string, object>();
                foreach (var prop in logEvent.Properties)
                {
                    if (!excludedProperties.Contains(prop.Key))
                    {
                        additionalProperties[prop.Key] = prop.Value.ToString().Trim('"');
                    }
                }

                var propertiesJson = additionalProperties.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(additionalProperties)
                    : null;

                // Add parameters
                command.Parameters.AddWithValue("@when", logEvent.Timestamp.DateTime);
                command.Parameters.AddWithValue("@level", level);
                command.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                command.Parameters.AddWithValue("@requestId", (object?)requestId ?? DBNull.Value);
                command.Parameters.AddWithValue("@httpMethod", (object?)httpMethod ?? DBNull.Value);
                command.Parameters.AddWithValue("@statusCode", (object?)statusCode ?? DBNull.Value);
                command.Parameters.AddWithValue("@url", url);
                command.Parameters.AddWithValue("@durationMs", (object?)durationMs ?? DBNull.Value);
                command.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
                command.Parameters.AddWithValue("@userName", (object?)userName ?? DBNull.Value);
                command.Parameters.AddWithValue("@clientIp", (object?)clientIp ?? DBNull.Value);
                command.Parameters.AddWithValue("@userAgent", (object?)userAgent ?? DBNull.Value);
                command.Parameters.AddWithValue("@message", message);
                command.Parameters.AddWithValue("@exception", exception);
                command.Parameters.AddWithValue("@trace", trace);
                command.Parameters.AddWithValue("@logger", logger);
                command.Parameters.AddWithValue("@channel", "Api");
                command.Parameters.AddWithValue("@requestHeaders", (object?)requestHeaders ?? DBNull.Value);
                command.Parameters.AddWithValue("@requestBody", (object?)requestBody ?? DBNull.Value);
                command.Parameters.AddWithValue("@responseBody", (object?)responseBody ?? DBNull.Value);
                command.Parameters.AddWithValue("@properties", (object?)propertiesJson ?? DBNull.Value);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Silently fail to avoid logging recursion
                // In production, you might want to log to a file or use a fallback mechanism
                System.Diagnostics.Debug.WriteLine($"Failed to write log to PostgreSQL: {ex.Message}");
                throw;
            }
        }

        private static string? GetPropertyValue(LogEvent logEvent, string propertyName)
        {
            if (logEvent.Properties.TryGetValue(propertyName, out var property))
            {
                // Handle null values properly
                if (property is ScalarValue scalarValue && scalarValue.Value == null)
                {
                    return null;
                }
                
                var value = property.ToString().Trim('"');
                
                // Don't return "null" as a string - return actual null
                if (string.IsNullOrEmpty(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                
                return value;
            }
            return null;
        }

        private static int? GetIntPropertyValue(LogEvent logEvent, string propertyName)
        {
            if (logEvent.Properties.TryGetValue(propertyName, out var property))
            {
                if (property is ScalarValue scalarValue && scalarValue.Value is int intValue)
                {
                    return intValue;
                }
                if (int.TryParse(property.ToString().Trim('"'), out var parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        private static long? GetLongPropertyValue(LogEvent logEvent, string propertyName)
        {
            if (logEvent.Properties.TryGetValue(propertyName, out var property))
            {
                if (property is ScalarValue scalarValue && scalarValue.Value is long longValue)
                {
                    return longValue;
                }
                if (long.TryParse(property.ToString().Trim('"'), out var parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        private static string GetLevelAbbreviation(LogEventLevel level)
        {
            return level switch
            {
                LogEventLevel.Verbose => "Verbose",
                LogEventLevel.Debug => "Debug",
                LogEventLevel.Information => "Info",
                LogEventLevel.Warning => "Warn",
                LogEventLevel.Error => "Error",
                LogEventLevel.Fatal => "Fatal",
                _ => level.ToString().Substring(0, Math.Min(15, level.ToString().Length))
            };
        }
    }
}

