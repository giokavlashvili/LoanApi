namespace Application.Common.Confirmation
{
    /// <summary>
    /// The allowlist of commands that may be parked and replayed. Every resolution of a stored
    /// payload goes through here — <c>Type.GetType(storedName)</c> is never acceptable, because
    /// the stored value must survive the namespace renames a forked boilerplate will get.
    /// </summary>
    public interface IOperationTypeRegistry
    {
        /// <summary>Descriptor for a command type, or null if it is not confirmable.</summary>
        OperationTypeDescriptor? Find(Type commandType);

        /// <summary>
        /// Descriptor for a stored discriminator, or null. Null is the fail-closed path for a
        /// payload captured under a version that no longer exists — refuse it rather than
        /// deserialize into whatever the type looks like now.
        /// </summary>
        OperationTypeDescriptor? Find(string discriminator);
    }
}
