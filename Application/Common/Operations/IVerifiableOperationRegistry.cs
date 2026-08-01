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
        /// <c>UnknownVerifiableOperation</c> if the name is not registered — which must happen
        /// before anything is sent, not after.
        /// </summary>
        VerifiableOperationDescriptor Get(string name);

        bool TryGet(string name, out VerifiableOperationDescriptor? descriptor);

        /// <summary>Everything registered, for the startup log that makes the allowlist auditable.</summary>
        IReadOnlyCollection<VerifiableOperationDescriptor> All { get; }
    }
}
