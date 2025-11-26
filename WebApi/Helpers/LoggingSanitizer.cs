using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebApi.Helpers
{
    public static class LoggingSanitizer
    {
        private static readonly string[] SensitiveHeaderNames = 
        {
            "Authorization", "Cookie", "X-Api-Key", "X-Auth-Token", 
            "X-API-Key", "Api-Key", "X-CSRF-Token"
        };

        private static readonly string[] SensitivePropertyNames = 
        {
            "Password", "ConfirmPassword", "Token", "Secret", "ApiKey", 
            "PersonalNumber", "SSN", "CreditCard", "CardNumber", "CVV"
        };

        private static readonly string[] SensitiveQueryParams = 
        {
            "token", "key", "secret", "password", "apiKey", "apikey"
        };

        private const int MaxBodySize = 10 * 1024; // 10KB

        /// <summary>
        /// Sanitizes HTTP headers by removing sensitive headers.
        /// </summary>
        public static Dictionary<string, string> SanitizeHeaders(IHeaderDictionary headers)
        {
            var sanitized = new Dictionary<string, string>();
            
            foreach (var header in headers)
            {
                if (!SensitiveHeaderNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sanitized[header.Key] = string.Join(", ", header.Value.ToArray());
                }
                else
                {
                    sanitized[header.Key] = "***REDACTED***";
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Sanitizes request/response body by removing sensitive fields from JSON.
        /// </summary>
        public static string? SanitizeBody(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body;

            try
            {
                // Limit body size
                var limitedBody = LimitBodySize(body);

                // Try to parse as JSON
                using var doc = JsonDocument.Parse(limitedBody);
                var sanitized = SanitizeJsonElement(doc.RootElement);
                
                return JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                // If not JSON or parsing fails, return limited body as-is
                return LimitBodySize(body);
            }
        }

        /// <summary>
        /// Recursively sanitizes JSON elements by removing sensitive properties.
        /// </summary>
        private static object SanitizeJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => SanitizeJsonObject(element),
                JsonValueKind.Array => element.EnumerateArray().Select(SanitizeJsonElement).ToArray(),
                _ => element.GetRawText()
            };
        }

        /// <summary>
        /// Sanitizes JSON object by replacing sensitive property values.
        /// </summary>
        private static Dictionary<string, object> SanitizeJsonObject(JsonElement obj)
        {
            var result = new Dictionary<string, object>();

            foreach (var property in obj.EnumerateObject())
            {
                var propertyName = property.Name;
                var isSensitive = SensitivePropertyNames.Any(sensitive => 
                    propertyName.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

                if (isSensitive)
                {
                    result[propertyName] = "***REDACTED***";
                }
                else
                {
                    result[propertyName] = SanitizeJsonElement(property.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Limits body size to prevent log bloat.
        /// </summary>
        public static string LimitBodySize(string body)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            if (body.Length <= MaxBodySize)
                return body;

            return body.Substring(0, MaxBodySize) + "... [TRUNCATED]";
        }

        /// <summary>
        /// Sanitizes query string by removing sensitive parameters.
        /// </summary>
        public static string SanitizeQueryString(string? queryString)
        {
            if (string.IsNullOrWhiteSpace(queryString))
                return string.Empty;

            if (!queryString.Contains('='))
                return queryString;

            var parts = queryString.TrimStart('?').Split('&');
            var sanitized = new List<string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var keyValue = part.Split('=');
                if (keyValue.Length != 2)
                {
                    sanitized.Add(part);
                    continue;
                }

                var key = keyValue[0];
                var isSensitive = SensitiveQueryParams.Contains(key, StringComparer.OrdinalIgnoreCase);

                if (isSensitive)
                {
                    sanitized.Add($"{key}=***REDACTED***");
                }
                else
                {
                    sanitized.Add(part);
                }
            }

            return string.Join("&", sanitized);
        }
    }
}

