namespace Application.Common.Operations
{
    /// <summary>
    /// The allowlist of operations reachable through the generic <c>initiate</c>/<c>confirm</c>
    /// endpoints. Built once at startup from <see cref="VerifiableOperationAttribute"/>.
    /// </summary>
    public interface IVerifiableOperationRegistry
    {
        /// <summary>
        /// Throws <see cref="Domain.Exceptions.DomainValidationException"/> with
        /// <c>UnknownVerifiableOperation</c> if the operation is not registered — which must happen
        /// before anything is sent, not after. A defined enum member is not the same as a registered
        /// operation: registration comes from the attribute, not from the enum.
        /// </summary>
        VerifiableOperationDescriptor Get(VerifiableOperationType type);

        bool TryGet(VerifiableOperationType type, out VerifiableOperationDescriptor? descriptor);

        /// <summary>
        /// The read-back overload, for a member name loaded from a stored row. Fails closed: a row
        /// naming a member that no longer exists is refused rather than guessed at.
        /// </summary>
        VerifiableOperationDescriptor Get(string name);

        bool TryGet(string name, out VerifiableOperationDescriptor? descriptor);

        /// <summary>Everything registered, for the startup log that makes the allowlist auditable.</summary>
        IReadOnlyCollection<VerifiableOperationDescriptor> All { get; }
    }
}
