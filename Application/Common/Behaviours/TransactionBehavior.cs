namespace Application.Common.Behaviors;

using Application.Common.Interfaces;
using Application.Extensions;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// Wraps a command and everything it dispatches in one database transaction, so a flow that saves
/// more than once either lands completely or not at all. A single <c>SaveChanges</c> is already
/// atomic on its own — this earns its keep only for multi-save flows.
/// <para>
/// Opt <em>out</em>, not in: anything that is not an <see cref="IQuery{TResponse}"/> or an
/// <see cref="ISkipTransaction"/> is treated as a mutation and gets a transaction. A command that
/// forgot an opt-in marker would lose atomicity silently, which is the wrong direction to fail.
/// </para>
/// <para>
/// <strong>Registered innermost, after <c>OtpVerificationBehavior</c>.</strong> Anything that
/// performs an external side effect after a save — texting a code, sending mail, calling a payment
/// provider — must sit outside this behaviour, or carry <see cref="ISkipTransaction"/> when the
/// effect happens inside its own handler. Domain event handlers count: they are dispatched from
/// <c>SaveChangesAsync</c> and therefore run inside the transaction, which is why the ones in this
/// repo only log.
/// </para>
/// </summary>
// "notnull" rather than "TRequest : IRequest<TResponse>", for the reason spelled out on
// ValidationBehavior: MediatR.Contracts 2.x made IRequest and IRequest<T> unrelated, so a void
// command is not an IRequest<Unit>, the tighter constraint cannot be satisfied, and the container
// skips the registration in silence — leaving exactly the mutating commands that need a
// transaction without one. TransactionBehaviorTests covers this; do not tighten it back.
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
    private readonly IApplicationDbContext _dbContext;

    public TransactionBehavior(IApplicationDbContext dbContext,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // IQuery<out TResponse> is covariant, so the pattern match needs no reflection.
        // A nested Send inherits the outer transaction rather than opening a second one.
        if (request is IQuery<TResponse> or ISkipTransaction || _dbContext.HasActiveTransaction)
            return await next();

        // No Database.CreateExecutionStrategy() wrapper here, deliberately. It is inert while
        // UseSqlServer is configured without EnableRetryOnFailure, and wrapping would mean
        // ExecuteAsync replays the whole delegate — next() included — so a transient failure would
        // run the handler twice, silently. Without it, enabling retries makes EF throw an explicit
        // "does not support user-initiated transactions" error instead, which is the better
        // failure: loud, immediate, and it forces the idempotency question to be answered first.
        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        using (_logger.BeginScope(new List<KeyValuePair<string, object>> { new("TransactionContext", transaction.TransactionId) }))
        {
            try
            {
                var response = await next();

                await _dbContext.CommitTransactionAsync(transaction, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                _dbContext.RollbackTransaction();

                // The request itself is never logged: it carries OtpCode and Password on some
                // commands, and Serilog destructuring does not go through LogRedactor.
                _logger.LogError(ex, "Rolled back transaction for {CommandName}", request.GetGenericTypeName());

                throw;
            }
        }
    }
}
