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
        /// The assembly scan reduces to this. Separate so tests can hand it one deliberately
        /// broken type — scanning an assembly would mean a single bad fixture type throws for
        /// every other test in the file.
        /// </summary>
        public static VerifiableOperationRegistry Build(IEnumerable<Type> types)
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
        /// MediatR.Contracts 2.x made <c>IRequest</c> and <c>IRequest&lt;T&gt;</c> unrelated
        /// interfaces, so both have to be checked — the same split that forces every pipeline
        /// behaviour here onto a <c>notnull</c> constraint.
        /// </summary>
        private static bool IsMediatrRequest(Type type) =>
            typeof(IBaseRequest).IsAssignableFrom(type)
            || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
    }
}
