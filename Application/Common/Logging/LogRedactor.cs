using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Common.Logging
{
    /// <summary>
    /// Masks sensitive values before anything is handed to a log sink. Used by both the
    /// request/response middleware and <see cref="Behaviors.PerformanceBehavior{TRequest, TResponse}"/>,
    /// so a secret cannot leak through whichever path happens to log first.
    /// </summary>
    public static class LogRedactor
    {
        public const string Mask = "***REDACTED***";

        /// <summary>
        /// Property names masked everywhere, matched case-insensitively. Callers can supply
        /// their own set from configuration; this is the floor.
        /// </summary>
        public static readonly IReadOnlyCollection<string> DefaultSensitiveProperties = new[]
        {
            "password",
            "confirmPassword",
            "currentPassword",
            "newPassword",
            "oldPassword",
            "token",
            "accessToken",
            "refreshToken",
            "idToken",
            "authorization",
            "secret",
            "clientSecret",
            "apiKey",
            "personalNumber",
            "creditCard",
            "cardNumber",
            "cvv",
            "pin"
        };

        private static readonly ConcurrentDictionary<Type, string[]> AttributeMarkedProperties = new();

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            // The redacted output is read by humans in the Logs table, not deserialized back.
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Masks a captured HTTP body according to its media type. Structured payloads are
        /// parsed and masked by property name; unstructured text is passed through, since
        /// there are no field names to key off.
        /// </summary>
        public static string Redact(string body, string? mediaType, IReadOnlyCollection<string>? sensitiveProperties = null)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body;

            if (mediaType is null)
                return body;

            if (mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                return RedactFormUrlEncoded(body, sensitiveProperties);

            if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return RedactJson(body, sensitiveProperties);

            // text/plain, xml and friends: nothing reliable to key off, so leave as-is.
            return body;
        }

        /// <summary>
        /// Masks sensitive keys in an <c>application/x-www-form-urlencoded</c> payload.
        /// Form posts are the one place where a password arrives outside JSON.
        /// </summary>
        public static string RedactFormUrlEncoded(string body, IReadOnlyCollection<string>? sensitiveProperties = null)
        {
            if (string.IsNullOrWhiteSpace(body))
                return body;

            var keys = BuildKeySet(sensitiveProperties);

            var pairs = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair =>
                {
                    var separator = pair.IndexOf('=');

                    if (separator < 0)
                        return pair;

                    var name = pair[..separator];
                    return keys.Contains(Uri.UnescapeDataString(name)) ? $"{name}={Mask}" : pair;
                });

            return string.Join('&', pairs);
        }

        /// <summary>
        /// Masks sensitive properties in a JSON document. Returns a placeholder when the
        /// input will not parse, because an unparseable payload cannot be masked safely.
        /// </summary>
        public static string RedactJson(string json, IReadOnlyCollection<string>? sensitiveProperties = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            var keys = BuildKeySet(sensitiveProperties);

            try
            {
                var node = JsonNode.Parse(json, nodeOptions: null, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (node is null)
                    return json;

                RedactNode(node, keys);
                return node.ToJsonString(SerializerOptions);
            }
            catch (JsonException)
            {
                // Not JSON (or truncated mid-capture). Never return the raw text here: it may be
                // a form post carrying a password, which name based masking cannot reach.
                return "[unparseable body omitted]";
            }
        }

        /// <summary>
        /// Serializes a request/command object for logging with sensitive members masked.
        /// Honours both the name rules and <see cref="SensitiveDataAttribute"/>.
        /// </summary>
        public static string RedactObject(object? value, IReadOnlyCollection<string>? sensitiveProperties = null)
        {
            if (value is null)
                return "null";

            var keys = BuildKeySet(sensitiveProperties);

            foreach (var name in GetAttributeMarkedProperties(value.GetType()))
                keys.Add(name);

            try
            {
                var json = JsonSerializer.Serialize(value, value.GetType(), SerializerOptions);
                var node = JsonNode.Parse(json);

                if (node is null)
                    return "null";

                RedactNode(node, keys);
                return node.ToJsonString(SerializerOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // A type that will not serialize (streams, delegates) must not take the request
                // down just because it was slow enough to be logged.
                return $"[{value.GetType().Name} not serializable]";
            }
        }

        private static HashSet<string> BuildKeySet(IReadOnlyCollection<string>? sensitiveProperties)
        {
            var source = sensitiveProperties is { Count: > 0 } ? sensitiveProperties : DefaultSensitiveProperties;
            return new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
        }

        private static string[] GetAttributeMarkedProperties(Type type) =>
            AttributeMarkedProperties.GetOrAdd(type, static t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.IsDefined(typeof(SensitiveDataAttribute), inherit: true))
                .Select(p => p.Name)
                .ToArray());

        private static void RedactNode(JsonNode node, HashSet<string> keys)
        {
            switch (node)
            {
                case JsonObject obj:
                    // Materialize first: assigning to an indexer while enumerating mutates the collection.
                    foreach (var property in obj.ToList())
                    {
                        if (keys.Contains(property.Key))
                        {
                            obj[property.Key] = JsonValue.Create(Mask);
                        }
                        else if (property.Value is not null)
                        {
                            RedactNode(property.Value, keys);
                        }
                    }
                    break;

                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item is not null)
                            RedactNode(item, keys);
                    }
                    break;
            }
        }
    }
}
