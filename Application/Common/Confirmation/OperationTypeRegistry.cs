using Application.Common.Logging;
using Application.Common.Otp;
using System.Collections;
using System.Reflection;

namespace Application.Common.Confirmation
{
    /// <summary>
    /// Builds the confirmable-command allowlist by scanning an assembly once at startup, and
    /// refuses to build at all if anything about the declarations is wrong.
    /// <para>
    /// Everything here throws rather than skipping. A registry that quietly drops a malformed
    /// entry turns a startup error into a runtime one — the operation would look confirmable,
    /// park fine, and fail only when someone tried to confirm it.
    /// </para>
    /// </summary>
    public sealed class OperationTypeRegistry : IOperationTypeRegistry
    {
        private readonly Dictionary<string, OperationTypeDescriptor> _byDiscriminator;
        private readonly Dictionary<Type, OperationTypeDescriptor> _byType;

        public OperationTypeRegistry(params Assembly[] assemblies)
        {
            _byDiscriminator = new Dictionary<string, OperationTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
            _byType = new Dictionary<Type, OperationTypeDescriptor>();

            foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsClass && !t.IsAbstract))
            {
                var attribute = type.GetCustomAttribute<ConfirmableOperationAttribute>();
                var marked = typeof(IRequireOperationConfirmation).IsAssignableFrom(type);

                if (attribute is null && !marked)
                    continue;

                // Either half alone is a mistake that would otherwise surface much later: the
                // attribute without the interface is never gated, the interface without the
                // attribute cannot be given a discriminator.
                if (attribute is null)
                    throw new InvalidOperationException(
                        $"{type.FullName} implements {nameof(IRequireOperationConfirmation)} but has no [{nameof(ConfirmableOperationAttribute)}].");

                if (!marked)
                    throw new InvalidOperationException(
                        $"{type.FullName} has [{nameof(ConfirmableOperationAttribute)}] but does not implement {nameof(IRequireOperationConfirmation)}.");

                if (typeof(IRequireOtpVerification).IsAssignableFrom(type))
                    throw new InvalidOperationException(
                        $"{type.FullName} uses both confirmation topologies. A command is either gated inline by " +
                        $"{nameof(IRequireOtpVerification)} or parked by {nameof(IRequireOperationConfirmation)}; " +
                        "stacking them issues two codes for one action.");

                if (string.IsNullOrWhiteSpace(attribute.Discriminator))
                    throw new InvalidOperationException($"{type.FullName} declares a blank operation discriminator.");

                if (_byDiscriminator.TryGetValue(attribute.Discriminator, out var clash))
                    throw new InvalidOperationException(
                        $"Operation discriminator '{attribute.Discriminator}' is declared by both " +
                        $"{clash.CommandType.FullName} and {type.FullName}. It is persisted on every parked row, so it must be unique.");

                EnsureNothingSensitiveIsCaptured(type);

                var descriptor = new OperationTypeDescriptor(
                    attribute.Discriminator,
                    type,
                    attribute.Policy,
                    ParseLifetime(type, attribute.Lifetime));

                _byDiscriminator.Add(descriptor.Discriminator, descriptor);
                _byType.Add(type, descriptor);
            }
        }

        public OperationTypeDescriptor? Find(Type commandType) =>
            _byType.TryGetValue(commandType, out var descriptor) ? descriptor : null;

        public OperationTypeDescriptor? Find(string discriminator) =>
            _byDiscriminator.TryGetValue(discriminator, out var descriptor) ? descriptor : null;

        private static TimeSpan? ParseLifetime(Type type, string? lifetime)
        {
            if (string.IsNullOrWhiteSpace(lifetime))
                return null;

            if (!TimeSpan.TryParse(lifetime, out var parsed) || parsed <= TimeSpan.Zero)
                throw new InvalidOperationException(
                    $"{type.FullName} declares an unusable operation lifetime '{lifetime}'.");

            return parsed;
        }

        /// <summary>
        /// A parked operation stores its command as JSON, so anything on the command is written to
        /// the database and lives there until the operation is resolved. A secret must therefore
        /// never appear on a confirmable command — which is exactly why registration is the place
        /// to check, rather than trusting whoever adds the next one to remember.
        /// <para>
        /// Both rules are needed. <see cref="SensitiveDataAttribute"/> catches what is marked;
        /// <see cref="LogRedactor.DefaultSensitiveProperties"/> catches what is merely named like a
        /// secret, which is how <c>RegisterUserCommand.Password</c> is protected today — it carries
        /// no attribute, and an attribute-only scan would wave through the single most important
        /// case this guard exists for.
        /// </para>
        /// </summary>
        private static void EnsureNothingSensitiveIsCaptured(Type commandType)
        {
            Walk(commandType, commandType, commandType.Name, new HashSet<Type>());

            static void Walk(Type commandType, Type type, string path, HashSet<Type> seen)
            {
                // Recursion is what makes this worth having: a secret one level down, on a nested
                // DTO, is captured into the payload exactly the same way a top level one is.
                if (!seen.Add(type))
                    return;

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length > 0)
                        continue;

                    var location = $"{path}.{property.Name}";

                    if (property.GetCustomAttribute<SensitiveDataAttribute>() is not null)
                        throw SensitiveMember(commandType, location, $"is marked [{nameof(SensitiveDataAttribute)}]");

                    if (LogRedactor.DefaultSensitiveProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        throw SensitiveMember(commandType, location, "is named like a secret");

                    var next = Unwrap(property.PropertyType);

                    if (next is not null)
                        Walk(commandType, next, location, seen);
                }
            }

            static InvalidOperationException SensitiveMember(Type commandType, string location, string why) =>
                new($"{commandType.FullName} cannot be a confirmable operation: {location} {why}, and a parked " +
                    "operation persists its command as JSON. Keep secret-bearing commands on the inline " +
                    $"{nameof(IRequireOtpVerification)} gate, which stores only a hash.");

            // Null for anything whose value is written as a JSON scalar; otherwise the type to
            // descend into. Collections are unwrapped to their element type so a List<T> of DTOs
            // is inspected rather than skipped.
            static Type? Unwrap(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;

                if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                    || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
                    || type == typeof(Guid) || type == typeof(Uri) || type == typeof(object))
                    return null;

                if (type.IsArray)
                    return type.GetElementType();

                if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    var element = type.GetInterfaces()
                        .Concat(new[] { type })
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        ?.GetGenericArguments()[0];

                    return element is null ? null : Unwrap(element);
                }

                return type;
            }
        }
    }
}
