namespace Application.Common.Confirmation
{
    /// <inheritdoc cref="IOperationExecutionContext"/>
    public sealed class OperationExecutionContext : IOperationExecutionContext
    {
        public bool IsConfirmedExecution => OperationId is not null;

        public Guid? OperationId { get; private set; }

        public IDisposable BeginConfirmedExecution(Guid operationId)
        {
            var previous = OperationId;
            OperationId = operationId;

            return new Scope(this, previous);
        }

        /// <remarks>
        /// Restores rather than clears, so a confirmed command that dispatches a nested request
        /// leaves the outer execution's state intact when the inner scope ends.
        /// </remarks>
        private sealed class Scope : IDisposable
        {
            private readonly OperationExecutionContext _owner;
            private readonly Guid? _previous;
            private bool _disposed;

            public Scope(OperationExecutionContext owner, Guid? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.OperationId = _previous;
            }
        }
    }
}
