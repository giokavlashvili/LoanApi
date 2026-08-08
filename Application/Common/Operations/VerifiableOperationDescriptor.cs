namespace Application.Common.Operations
{
    /// <summary>
    /// One registered operation, resolved once at startup so nothing reflects per request.
    /// </summary>
    public sealed class VerifiableOperationDescriptor
    {
        /// <summary>What the caller sends, and what the registry is keyed by.</summary>
        public required VerifiableOperationType Type { get; init; }

        /// <summary>
        /// <see cref="Type"/>'s member name — the OTP purpose and the value stored on the pending
        /// row. Everything that leaves this process as text uses this rather than the numeric value.
        /// </summary>
        public string Name => Type.ToString();

        /// <summary>
        /// The type an incoming payload binds to. Always taken from here, <strong>never</strong>
        /// from a type name in the request — deserializing a caller-named type is a remote code
        /// execution vector, and there is no version of this endpoint where that is acceptable.
        /// </summary>
        public required Type PayloadType { get; init; }

        public required bool RequiresAuthentication { get; init; }

        public required bool AllowsCallerSuppliedRecipient { get; init; }

        public required IReadOnlyList<string> RequiredPolicies { get; init; }

        /// <summary>
        /// Runs the operation. Built once at startup rather than resolved per call: dispatch goes
        /// through <c>ISender.Send(object, CancellationToken)</c>, which MediatR provides for
        /// exactly this, so the handler still gets <c>ValidationBehavior</c> and
        /// <c>PerformanceBehavior</c>. The response is type-erased — that is inherent to one
        /// endpoint returning every operation's result.
        /// </summary>
        public required Func<object, IServiceProvider, CancellationToken, Task<object?>> Execute { get; init; }
    }
}
