using Application.Common.Interfaces;
using Application.Common.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Application.Common.Serialization
{
    /// <summary>
    /// <inheritdoc cref="IProtectedPayloadSerializer"/>
    /// <para>
    /// Built on System.Text.Json contract customization rather than a hand-rolled walk over a
    /// <c>JsonNode</c>. A resolver modifier inspects every property as its type's contract is
    /// first built and swaps in an encrypting converter for anything marked
    /// <see cref="SensitiveDataAttribute"/>. Two consequences, and both are why this shape was
    /// chosen:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <strong>It recurses for free.</strong> Every type in the graph gets its own contract, so a
    /// sensitive property on a nested object or a collection element is found without any
    /// traversal code — and cannot be missed because someone forgot to recurse.
    /// </item>
    /// <item>
    /// <strong>Encrypt and decrypt cannot drift.</strong> They are the same converter in two
    /// directions, reached through the same options instance, so a property can never be written
    /// encrypted and read as though it were plaintext.
    /// </item>
    /// </list>
    /// <para>
    /// The options instance is built once and held, which is not a micro-optimisation:
    /// System.Text.Json caches contract metadata per <see cref="JsonSerializerOptions"/> instance,
    /// so constructing one per call would rebuild every contract on every request.
    /// </para>
    /// </summary>
    public sealed class ProtectedPayloadSerializer : IProtectedPayloadSerializer
    {
        private readonly JsonSerializerOptions _options;

        public ProtectedPayloadSerializer(IPayloadProtector protector)
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(typeInfo => ProtectSensitiveProperties(typeInfo, protector));

            // Web defaults so the stored JSON matches what the rest of the pipeline produces --
            // camelCase, case-insensitive reads -- and a payload written by one is readable by the
            // other if this ever has to be turned off.
            _options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = resolver
            };
        }

        public string Serialize(object? value, Type type) => JsonSerializer.Serialize(value, type, _options);

        public object? Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, type, _options);

        private static void ProtectSensitiveProperties(JsonTypeInfo typeInfo, IPayloadProtector protector)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (var property in typeInfo.Properties)
            {
                if (property.AttributeProvider?.IsDefined(typeof(SensitiveDataAttribute), inherit: true) != true)
                    continue;

                property.CustomConverter = CreateConverter(property.PropertyType, protector);
            }
        }

        private static JsonConverter CreateConverter(Type propertyType, IPayloadProtector protector) =>
            (JsonConverter)Activator.CreateInstance(
                typeof(ProtectedConverter<>).MakeGenericType(propertyType), protector)!;

        /// <summary>
        /// Encrypts the property's <em>serialized</em> form rather than assuming it is a string.
        /// That costs nothing for strings and is what lets <c>[SensitiveData] DateTime? BirthDate</c>
        /// work — date of birth is exactly the kind of field this feature exists for, and a
        /// string-only converter would have quietly refused it.
        /// </summary>
        private sealed class ProtectedConverter<T> : JsonConverter<T>
        {
            /// <summary>
            /// A plain options instance for the inner round trip. Reusing the outer one would send
            /// the value back through this same converter and recurse forever; it also means a
            /// sensitive property holding a complex object is encrypted once, as a whole, rather
            /// than field by field inside an envelope that is already opaque.
            /// </summary>
            private static readonly JsonSerializerOptions Inner = new(JsonSerializerDefaults.Web);

            private readonly IPayloadProtector _protector;

            public ProtectedConverter(IPayloadProtector protector)
            {
                _protector = protector;
            }

            public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return default;

                // A protected value is always a JSON string. Anything else is a row written before
                // this property was marked [SensitiveData] -- an operation still pending across the
                // deployment that added the attribute. Reading it as a string would throw
                // InvalidOperationException, which BindStoredAsync does not catch, so a stuck
                // operation would surface as a 500 instead of a clean "start over". Raised as a
                // CryptographicException so it joins every other unreadable-payload case.
                if (reader.TokenType != JsonTokenType.String)
                    throw new CryptographicException(
                        $"Expected a protected string for a [SensitiveData] property but found {reader.TokenType}. " +
                        "This payload was most likely stored before the property was marked sensitive.");

                var protectedValue = reader.GetString();

                if (string.IsNullOrEmpty(protectedValue))
                    return default;

                return JsonSerializer.Deserialize<T>(_protector.Unprotect(protectedValue), Inner);
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(_protector.Protect(JsonSerializer.Serialize(value, Inner)));
            }
        }
    }
}
