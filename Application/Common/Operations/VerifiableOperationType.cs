namespace Application.Common.Operations
{
    /// <summary>
    /// The closed set of operation names the generic <c>initiate</c>/<c>confirm</c> endpoints accept.
    /// <para>
    /// The reason this is an enum rather than a <c>const string</c> is that it <strong>crosses the
    /// wire</strong>: NSwag renders it as an enum schema, so <c>operationType</c> becomes a generated
    /// TypeScript enum and a client cannot express an operation the server does not have. A const
    /// would give the same IntelliSense in C# and nothing at all to the caller, which is the half
    /// that needed fixing.
    /// </para>
    /// <para>
    /// <strong>Members are persisted by name</strong>, in <c>PendingOperations.OperationType</c> and
    /// in <c>OtpVerifications.Purpose</c>. Two consequences follow and neither is optional:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Renaming a member is a <em>breaking change</em>, not a refactor. Rows written before the
    /// rename and codes already issued against the old purpose stop resolving, so treat it like a
    /// data migration — or add a new member and retire the old one.
    /// </item>
    /// <item>
    /// Values exist only to keep the enum well-formed; nothing persists or transmits them, so they
    /// are safe to reorder. They start at 1 deliberately: <c>InitiateOperationCommand.OperationType</c>
    /// is nullable so that an omitted field is rejected rather than silently defaulting to whichever
    /// operation happens to sit at 0.
    /// </item>
    /// </list>
    /// <para>
    /// Adding a member here does <strong>not</strong> expose anything. An operation becomes reachable
    /// only by carrying <see cref="VerifiableOperationAttribute"/>; a member no type claims is simply
    /// unregistered, and the OpenAPI document narrows the published set to what the registry actually
    /// holds — see <c>VerifiableOperationPayloadsDocumentProcessor</c>.
    /// </para>
    /// </summary>
    public enum VerifiableOperationType
    {
        /// <summary>
        /// The worked example. See <c>Application.LoanApplications.Commands.DeleteApplicationCommand</c>.
        /// </summary>
        DeleteLoanApplication = 1
    }
}
