namespace Application.Common.Confirmation
{
    /// <summary>One registered confirmable command, resolved once at startup.</summary>
    public sealed class OperationTypeDescriptor
    {
        public OperationTypeDescriptor(
            string discriminator,
            Type commandType,
            ConfirmationPolicy policy,
            TimeSpan? lifetime)
        {
            Discriminator = discriminator;
            CommandType = commandType;
            Policy = policy;
            Lifetime = lifetime;
        }

        public string Discriminator { get; }

        public Type CommandType { get; }

        public ConfirmationPolicy Policy { get; }

        /// <summary>Per-operation override, or null to use <c>PendingOperations:DefaultLifetime</c>.</summary>
        public TimeSpan? Lifetime { get; }
    }
}
