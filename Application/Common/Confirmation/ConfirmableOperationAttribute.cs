namespace Application.Common.Confirmation
{
    /// <summary>
    /// Declares the registry entry for a command marked
    /// <see cref="IRequireOperationConfirmation"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ConfirmableOperationAttribute : Attribute
    {
        public ConfirmableOperationAttribute(string discriminator)
        {
            Discriminator = discriminator;
        }

        /// <summary>
        /// Stable, versioned identity for the operation, e.g. <c>loan.status.update.v1</c>. It is
        /// persisted on every parked row, so it must not be derived from the CLR type name — a
        /// namespace rename would orphan them.
        /// <para>
        /// Bump the version suffix when the command's shape changes incompatibly, and let the old
        /// discriminator fall out of the registry. Rows captured under it then fail closed at
        /// confirm time instead of deserializing with a default for a field that did not exist
        /// when they were parked.
        /// </para>
        /// </summary>
        public string Discriminator { get; }

        /// <summary>Who may confirm. See <see cref="ConfirmationPolicy.SameUser"/> before changing it.</summary>
        public ConfirmationPolicy Policy { get; init; } = ConfirmationPolicy.SameUser;

        /// <summary>
        /// Overrides <c>PendingOperations:DefaultLifetime</c> for this operation. Expressed as a
        /// parseable TimeSpan because attribute arguments must be constants.
        /// </summary>
        public string? Lifetime { get; init; }
    }
}
