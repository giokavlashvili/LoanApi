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
        private readonly IReadOnlyDictionary<string, VerifiableOperationDescriptor> _byName;

        private VerifiableOperationRegistry(IReadOnlyDictionary<string, VerifiableOperationDescriptor> byName)
        {
            _byName = byName;
        }

        public IReadOnlyCollection<VerifiableOperationDescriptor> All => _byName.Values.ToList();

        public VerifiableOperationDescriptor Get(string name)
        {
            if (!TryGet(name, out var descriptor))
                throw new DomainValidationException("UnknownVerifiableOperation");

            return descriptor!;
        }

        public bool TryGet(string name, out VerifiableOperationDescriptor? descriptor)
        {
            descriptor = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return _byName.TryGetValue(name, out descriptor);
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
            var byName = new Dictionary<string, VerifiableOperationDescriptor>(StringComparer.Ordinal);

            var candidates = types
                .Select(t => (Type: t, Attribute: t.GetCustomAttribute<VerifiableOperationAttribute>()))
                .Where(x => x.Attribute is not null);

            foreach (var (type, attribute) in candidates)
            {
                var name = attribute!.Name;

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException(
                        $"[VerifiableOperation] on '{type.FullName}' has an empty name.");

                if (byName.ContainsKey(name))
                    throw new InvalidOperationException(
                        $"Two operations are registered as '{name}' — '{type.FullName}' and " +
                        $"'{byName[name].PayloadType.FullName}'. Names address operations remotely, so they must be unique.");

                // The trap. Dispatching this at confirm re-enters OtpVerificationBehavior and
                // issues a second challenge, so the caller can never get through.
                if (typeof(IRequireOtpVerification).IsAssignableFrom(type))
                    throw new InvalidOperationException(
                        $"'{type.FullName}' is registered as verifiable operation '{name}' but also implements " +
                        $"{nameof(IRequireOtpVerification)}. Use one mechanism or the other: gating it twice issues a " +
                        "second challenge from inside confirm, which costs two messages per attempt and never succeeds.");

                if (!IsMediatrRequest(type))
                    throw new InvalidOperationException(
                        $"'{type.FullName}' is registered as verifiable operation '{name}' but is not an IRequest or " +
                        "IRequest<T>. Dispatch is dynamic, so this would only fail on a live request.");

                byName[name] = new VerifiableOperationDescriptor
                {
                    Name = name,
                    PayloadType = type,
                    RequiresAuthentication = attribute.RequiresAuthentication,
                    AllowsCallerSuppliedRecipient = attribute.AllowsCallerSuppliedRecipient,
                    RequiredPolicies = attribute.RequiredPolicies,
                    Execute = (payload, services, cancellationToken) =>
                        services.GetRequiredService<ISender>().Send(payload, cancellationToken)
                };
            }

            return new VerifiableOperationRegistry(byName);
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
