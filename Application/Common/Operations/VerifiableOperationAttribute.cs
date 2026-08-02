namespace Application.Common.Operations
{
    /// <summary>
    /// Marks a command as executable through the generic <c>initiate</c>/<c>confirm</c> endpoints,
    /// under the name given here.
    /// <para>
    /// This is an <strong>allowlist, not a convenience</strong>. Anything carrying this attribute
    /// becomes remotely executable by name, so adding it is a security decision. The registry is
    /// built by scanning for it, and every registered operation is logged at startup so what is
    /// exposed can be read off the boot log of any environment.
    /// </para>
    /// <para>
    /// The marked type must <strong>not</strong> implement <c>IRequireOtpVerification</c>.
    /// Dispatching such a command at confirm re-enters <c>OtpVerificationBehavior</c>, issues a
    /// second challenge and throws 428 from inside the confirm call — the user verifies a code and
    /// is asked for another, at two messages an attempt, until the hourly cap locks them out.
    /// <c>VerifiableOperationRegistry.Build</c> rejects it at startup rather than in production.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class VerifiableOperationAttribute : Attribute
    {
        public VerifiableOperationAttribute(VerifiableOperationType type) => Type = type;

        /// <summary>
        /// The value clients send as <c>operationType</c>. An enum rather than a string so the
        /// closed set reaches the generated client — see <see cref="VerifiableOperationType"/>.
        /// </summary>
        public VerifiableOperationType Type { get; }

        /// <summary>
        /// The persisted and hashed form of <see cref="Type"/>: the OTP purpose the challenge is
        /// scoped to, so a code issued for one operation can never be spent on another, and the
        /// value written to <c>PendingOperations.OperationType</c>. Deliberately the member
        /// <em>name</em> and not its numeric value — a number would make stored rows unreadable and
        /// turn reordering the enum into silent data corruption.
        /// </summary>
        public string Name => Type.ToString();

        public bool RequiresAuthentication { get; init; } = true;

        /// <summary>
        /// Whether the caller may name the recipient. Left <c>false</c>, the code goes to the
        /// number on the authenticated account, which is what stops a caller redirecting their own
        /// confirmation to a phone they control. Set it only where no account exists yet.
        /// </summary>
        public bool AllowsCallerSuppliedRecipient { get; init; }

        /// <summary>
        /// Policies checked at initiate <em>and</em> again at confirm. Minutes pass between the
        /// two, which is long enough for a role to be revoked.
        /// </summary>
        public string[] RequiredPolicies { get; init; } = [];
    }
}
