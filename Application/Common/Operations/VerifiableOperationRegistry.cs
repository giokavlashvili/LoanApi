using Application.Common.Logging;
using Application.Common.Otp;
using Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Application.Common.Operations
{
    /// <inheritdoc cref="IVerifiableOperationRegistry"/>
    public sealed class VerifiableOperationRegistry : IVerifiableOperationRegistry
    {
        private readonly IReadOnlyDictionary<VerifiableOperationType, VerifiableOperationDescriptor> _byType;

        private VerifiableOperationRegistry(IReadOnlyDictionary<VerifiableOperationType, VerifiableOperationDescriptor> byType)
        {
            _byType = byType;
        }

        public IReadOnlyCollection<VerifiableOperationDescriptor> All => _byType.Values.ToList();

        public VerifiableOperationDescriptor Get(VerifiableOperationType type)
        {
            if (!TryGet(type, out var descriptor))
                throw new DomainValidationException("UnknownVerifiableOperation");

            return descriptor!;
        }

        public bool TryGet(VerifiableOperationType type, out VerifiableOperationDescriptor? descriptor) =>
            _byType.TryGetValue(type, out descriptor);

        public VerifiableOperationDescriptor Get(string name)
        {
            if (!TryGet(name, out var descriptor))
                throw new DomainValidationException("UnknownVerifiableOperation");

            return descriptor!;
        }

        /// <summary>
        /// The read-back path: <c>PendingOperations.OperationType</c> holds a member name, and a row
        /// written before a deploy may name a member that deploy deleted. Parsing here rather than
        /// at the call site keeps that one concern in one place.
        /// <para>
        /// <c>ignoreCase: false</c> on purpose. Names address operations remotely, and quietly
        /// accepting a different casing widens what the allowlist matches.
        /// </para>
        /// <para>
        /// The round-trip check is not redundant. <c>Enum.TryParse</c> also accepts numeric text
        /// and comma-separated lists, so without it <c>"1"</c> would resolve to whichever member
        /// holds that value — an address into the allowlist that nothing ever writes and no caller
        /// should be able to use.
        /// </para>
        /// </summary>
        public bool TryGet(string name, out VerifiableOperationDescriptor? descriptor)
        {
            descriptor = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return Enum.TryParse<VerifiableOperationType>(name, ignoreCase: false, out var type)
                && string.Equals(type.ToString(), name, StringComparison.Ordinal)
                && TryGet(type, out descriptor);
        }

        /// <summary>
        /// Scans for <see cref="VerifiableOperationAttribute"/> and validates every finding.
        /// <para>
        /// Everything here throws at startup rather than deferring to first use. Each of these
        /// failures is silent or expensive at runtime: an unsatisfiable dispatch surfaces as a
        /// MediatR error on a live request, a duplicate name means one operation shadows another,
        /// and an OTP-gated operation deadlocks the caller in a 428 loop that bills two messages
        /// per attempt.
        /// </para>
        /// </summary>
        public static VerifiableOperationRegistry Build(params Assembly[] assemblies) =>
            Build(assemblies.SelectMany(a => a.GetTypes()));

        /// <summary>
        /// Scan overload carrying the two checks that need to know about the wider application:
        /// whether payload encryption is available, and which property names the log redactor
        /// masks. See <see cref="ValidateSensitiveProperties"/>.
        /// </summary>
        public static VerifiableOperationRegistry Build(
            Assembly assembly,
            bool payloadProtectionConfigured,
            IReadOnlyCollection<string> redactedPropertyNames) =>
            Build(assembly.GetTypes(), payloadProtectionConfigured, redactedPropertyNames);

        /// <summary>
        /// The assembly scan reduces to this. Separate so tests can hand it one deliberately
        /// broken type — scanning an assembly would mean a single bad fixture type throws for
        /// every other test in the file.
        /// </summary>
        public static VerifiableOperationRegistry Build(
            IEnumerable<Type> types,
            bool payloadProtectionConfigured = true,
            IReadOnlyCollection<string>? redactedPropertyNames = null)
        {
            var byType = new Dictionary<VerifiableOperationType, VerifiableOperationDescriptor>();

            var candidates = types
                .Select(t => (Type: t, Attribute: t.GetCustomAttribute<VerifiableOperationAttribute>()))
                .Where(x => x.Attribute is not null);

            foreach (var (type, attribute) in candidates)
            {
                var operationType = attribute!.Type;

                // Enums are not closed at runtime -- (VerifiableOperationType)999 is a legal cast and
                // a legal attribute argument. Left unchecked it would register under a name like
                // "999", which is then persisted and hashed as an OTP purpose.
                if (!Enum.IsDefined(operationType))
                    throw new InvalidOperationException(
                        $"[VerifiableOperation] on '{type.FullName}' has the undefined value {(int)operationType}. " +
                        $"Add a member to {nameof(VerifiableOperationType)} instead of casting.");

                if (byType.ContainsKey(operationType))
                    throw new InvalidOperationException(
                        $"Two operations are registered as '{operationType}' — '{type.FullName}' and " +
                        $"'{byType[operationType].PayloadType.FullName}'. Names address operations remotely, so they must be unique.");

                // The trap. Dispatching this at confirm re-enters OtpVerificationBehavior and
                // issues a second challenge, so the caller can never get through.
                if (typeof(IRequireOtpVerification).IsAssignableFrom(type))
                    throw new InvalidOperationException(
                        $"'{type.FullName}' is registered as verifiable operation '{operationType}' but also implements " +
                        $"{nameof(IRequireOtpVerification)}. Use one mechanism or the other: gating it twice issues a " +
                        "second challenge from inside confirm, which costs two messages per attempt and never succeeds.");

                if (!IsMediatrRequest(type))
                    throw new InvalidOperationException(
                        $"'{type.FullName}' is registered as verifiable operation '{operationType}' but is not an IRequest or " +
                        "IRequest<T>. Dispatch is dynamic, so this would only fail on a live request.");

                ValidateSensitiveProperties(type, operationType, payloadProtectionConfigured, redactedPropertyNames);

                byType[operationType] = new VerifiableOperationDescriptor
                {
                    Type = operationType,
                    PayloadType = type,
                    RequiresAuthentication = attribute.RequiresAuthentication,
                    AllowsCallerSuppliedRecipient = attribute.AllowsCallerSuppliedRecipient,
                    RequiredPolicies = attribute.RequiredPolicies,
                    Execute = (payload, services, cancellationToken) =>
                        services.GetRequiredService<ISender>().Send(payload, cancellationToken)
                };
            }

            return new VerifiableOperationRegistry(byType);
        }

        /// <summary>
        /// Two checks over a payload type's <see cref="SensitiveDataAttribute"/> properties, both
        /// guarding a failure that is otherwise silent and only visible in a breach.
        /// <list type="number">
        /// <item>
        /// <strong>Marked but unencryptable.</strong> No configured secret means the property is
        /// stored in the clear, which is exactly what marking it was meant to prevent. Refusing to
        /// start is the only signal that cannot be missed.
        /// </item>
        /// <item>
        /// <strong>Encrypted at rest but logged in the clear.</strong> This is the subtle one.
        /// <c>LogRedactor</c> masks nested JSON properly, but it derives its attribute-driven key
        /// names from the <em>top-level</em> type it is handed — which for this flow is
        /// <c>InitiateOperationCommand</c>, whose <c>Payload</c> is a shapeless <c>JsonElement</c>.
        /// So a nested <c>[SensitiveData]</c> property contributes no name, and only the static
        /// list stands between it and the <c>Logs</c> table. Encrypting a value in the database
        /// while writing it verbatim to a log row is worse than not encrypting it, because it
        /// looks handled.
        /// </item>
        /// </list>
        /// <para>
        /// The pairing is the same rule the <c>add-otp-gate</c> skill already states for OTP
        /// members — "must appear in <strong>both</strong>" — enforced here rather than merely
        /// documented. Auto-registering the names would be less friction but would hide the
        /// coupling; a developer who marks a property should learn that redaction is a separate
        /// list they now own.
        /// </para>
        /// </summary>
        private static void ValidateSensitiveProperties(
            Type type,
            VerifiableOperationType operationType,
            bool payloadProtectionConfigured,
            IReadOnlyCollection<string>? redactedPropertyNames)
        {
            var sensitive = FindSensitiveProperties(type, []).ToList();

            if (sensitive.Count == 0)
                return;

            if (!payloadProtectionConfigured)
                throw new InvalidOperationException(
                    $"Verifiable operation '{operationType}' ('{type.FullName}') has [SensitiveData] properties " +
                    $"({string.Join(", ", sensitive)}) but PayloadProtection:Secret is not configured. Its payload would " +
                    "be stored unencrypted, which is what marking those properties was meant to prevent. Configure the " +
                    "secret, or use IRequireOtpVerification instead -- it persists no payload at all.");

            if (redactedPropertyNames is null)
                return;

            var redacted = new HashSet<string>(redactedPropertyNames, StringComparer.OrdinalIgnoreCase);
            var unredacted = sensitive.Where(name => !redacted.Contains(name)).ToList();

            if (unredacted.Count > 0)
                throw new InvalidOperationException(
                    $"Verifiable operation '{operationType}' ('{type.FullName}') marks {string.Join(", ", unredacted)} " +
                    "[SensitiveData], but those names are not masked by the log redactor -- so they would be encrypted " +
                    "in the database and written verbatim to the Logs table on every initiate. Add them to " +
                    "RequestLogging:SensitiveProperties in appsettings.json, or to " +
                    $"{nameof(LogRedactor)}.{nameof(LogRedactor.DefaultSensitiveProperties)}.");
        }

        /// <summary>
        /// Walks into complex property types as well, since a sensitive value is just as exposed
        /// one level down. <paramref name="visited"/> is what keeps a self-referencing DTO from
        /// recursing forever.
        /// <para>
        /// Collections are unwrapped to their element type first. Skipping them — which an earlier
        /// version did, because <c>List&lt;T&gt;</c> lives in a <c>System.*</c> namespace like every
        /// other framework type — left the guard strictly weaker than the encryption it guards:
        /// <c>ProtectedPayloadSerializer</c> applies per contract, so it encrypts a marked property
        /// inside a collection element quite happily, and the redaction check would never have seen
        /// the name. Encrypted at rest, plaintext in the log, no warning. Exactly the outcome this
        /// exists to prevent.
        /// </para>
        /// </summary>
        private static IEnumerable<string> FindSensitiveProperties(Type type, HashSet<Type> visited)
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

                // Primitives and framework types have nothing of ours to find, and descending into
                // them would walk half the BCL. Checked *after* unwrapping, so a collection is
                // judged by what it holds rather than by the namespace it is declared in.
                if (propertyType.IsPrimitive || propertyType.IsEnum || propertyType == typeof(string)
                    || propertyType.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                foreach (var nested in FindSensitiveProperties(propertyType, visited))
                    yield return nested;
            }
        }

        /// <summary>
        /// The type actually carrying data: <c>int?</c> to <c>int</c>, <c>List&lt;Dto&gt;</c> and
        /// <c>Dto[]</c> to <c>Dto</c>, and repeatedly, so a
        /// <c>List&lt;List&lt;Dto&gt;&gt;</c> still resolves. <c>string</c> is excluded explicitly
        /// because it is an <c>IEnumerable&lt;char&gt;</c> and would otherwise unwrap to
        /// <c>char</c>.
        /// </summary>
        private static Type UnwrapElementType(Type type)
        {
            while (true)
            {
                var unwrapped = Nullable.GetUnderlyingType(type);

                if (unwrapped is not null)
                {
                    type = unwrapped;
                    continue;
                }

                if (type == typeof(string))
                    return type;

                if (type.IsArray)
                {
                    type = type.GetElementType()!;
                    continue;
                }

                var enumerable = type.GetInterfaces()
                    .Concat([type])
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                if (enumerable is null)
                    return type;

                type = enumerable.GetGenericArguments()[0];
            }
        }

        /// <summary>
        /// MediatR.Contracts 2.x made <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> unrelated
        /// interfaces, so both have to be checked — the same split that forces every pipeline
        /// behaviour here onto a <c>notnull</c> constraint.
        /// </summary>
        private static bool IsMediatrRequest(Type type) =>
            typeof(IBaseRequest).IsAssignableFrom(type)
            || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
    }
}
