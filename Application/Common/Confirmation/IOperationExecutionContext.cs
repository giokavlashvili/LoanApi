namespace Application.Common.Confirmation
{
    /// <summary>
    /// Carries the one fact the confirmation behaviour cannot safely take from the request: that
    /// this execution is the replay of an operation whose code has already been redeemed.
    /// <para>
    /// Scoped, and set only by <c>ConfirmOperationCommandHandler</c> immediately around its
    /// re-dispatch. Keeping it off the command is the whole defence — a flag on the request body
    /// is caller controlled, and forging it would skip confirmation entirely. See
    /// <see cref="IRequireOperationConfirmation"/>.
    /// </para>
    /// </summary>
    public interface IOperationExecutionContext
    {
        /// <summary>True only inside <see cref="BeginConfirmedExecution"/>.</summary>
        bool IsConfirmedExecution { get; }

        /// <summary>The operation being replayed, or null outside a confirmed execution.</summary>
        Guid? OperationId { get; }

        /// <summary>
        /// Opens a confirmed-execution scope. Disposing restores the previous state, so a
        /// confirmed command that itself dispatches another request cannot leak the flag.
        /// </summary>
        IDisposable BeginConfirmedExecution(Guid operationId);
    }
}
