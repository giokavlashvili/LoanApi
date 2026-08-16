using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

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
        /// extra names from configuration; those are merged into this set, never replace it.
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
            "pin",
            "otpCode",
            "otp",
            "code",
            "phoneNumber"
        };

        private static readonly ConcurrentDictionary<Type, string[]> AttributeMarkedProperties = new();

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            // The redacted output is read by humans in the Logs table, not deserialized back.
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Defaults plus any extra names. Config is additive so adding one name cannot
        /// accidentally unmask another.
        /// </summary>
        public static HashSet<string> MergeSensitiveProperties(IEnumerable<string>? additional)
        {
            var keys = new HashSet<string>(DefaultSensitiveProperties, StringComparer.OrdinalIgnoreCase);

            if (additional is null)
                return keys;

            foreach (var name in additional)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    keys.Add(name);
            }

            return keys;
        }

        /// <summary>
        /// Masks a captured HTTP body according to its media type. Structured payloads are
        /// parsed and masked by property or element name; unstructured text is passed through,
        /// since there are no field names to key off.
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

            if (mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return RedactXml(body, sensitiveProperties);

            // text/plain and friends: nothing reliable to key off, so leave as-is.
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
        /// Masks XML elements and attributes whose local name is on the sensitive list.
        /// External DTDs are refused so a request body cannot pull the parser off-box.
        /// </summary>
        public static string RedactXml(string xml, IReadOnlyCollection<string>? sensitiveProperties = null)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return xml;

            var keys = BuildKeySet(sensitiveProperties);

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };

                using var reader = XmlReader.Create(new StringReader(xml), settings);
                var document = XDocument.Load(reader);

                if (document.Root is not null)
                    RedactXElement(document.Root, keys);

                return document.ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException)
            {
                return "[unparseable body omitted]";
            }
        }

        /// <summary>
        /// Serializes a request/command object for logging with sensitive members masked.
        /// Honours both the name rules and <see cref="SensitiveDataAttribute"/> on the
        /// type and any nested public property types.
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

        private static HashSet<string> BuildKeySet(IReadOnlyCollection<string>? sensitiveProperties) =>
            MergeSensitiveProperties(sensitiveProperties);

        private static string[] GetAttributeMarkedProperties(Type type) =>
            AttributeMarkedProperties.GetOrAdd(type, static t => CollectAttributeMarkedProperties(t, []).ToArray());

        /// <summary>
        /// Walks the public property graph so a <c>[SensitiveData]</c> member two levels down
        /// still contributes its name. <paramref name="visited"/> stops a self-referencing DTO
        /// from looping. Collections are unwrapped to their element type first.
        /// <para>
        /// <c>InitiateOperationCommand.Payload</c> is a shapeless <c>JsonElement</c> and still
        /// contributes nothing — that path keeps relying on the name list.
        /// </para>
        /// </summary>
        private static IEnumerable<string> CollectAttributeMarkedProperties(Type type, HashSet<Type> visited)
        {
            if (!visited.Add(type))
                yield break;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.IsDefined(typeof(SensitiveDataAttribute), inherit: true))
                {
                    yield return property.Name;
                    continue;
                }

                var propertyType = UnwrapElementType(property.PropertyType);

                if (propertyType.IsPrimitive || propertyType.IsEnum || propertyType == typeof(string)
                    || propertyType.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                foreach (var nested in CollectAttributeMarkedProperties(propertyType, visited))
                    yield return nested;
            }
        }

        private static Type UnwrapElementType(Type type)
        {
            while (true)
            {
                var underlying = Nullable.GetUnderlyingType(type);
                if (underlying is not null)
                {
                    type = underlying;
                    continue;
                }

                if (type.IsArray)
                {
                    type = type.GetElementType()!;
                    continue;
                }

                if (type != typeof(string))
                {
                    var enumerable = type.GetInterfaces()
                        .Concat([type])
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                    if (enumerable is not null)
                    {
                        type = enumerable.GetGenericArguments()[0];
                        continue;
                    }
                }

                return type;
            }
        }

        private static void RedactXElement(XElement element, HashSet<string> keys)
        {
            foreach (var attribute in element.Attributes())
            {
                if (keys.Contains(attribute.Name.LocalName))
                    attribute.Value = Mask;
            }

            if (keys.Contains(element.Name.LocalName))
            {
                element.RemoveNodes();
                element.Value = Mask;
                return;
            }

            foreach (var child in element.Elements())
                RedactXElement(child, keys);
        }

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
