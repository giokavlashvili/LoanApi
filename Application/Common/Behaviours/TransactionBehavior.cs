namespace Application.Common.Behaviors;

using Application.Common.Interfaces;
using Application.Extensions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Wraps a command and everything it dispatches in one database transaction, so a flow that saves
/// more than once either lands completely or not at all. A single <c>SaveChanges</c> is already
/// atomic on its own — this earns its keep only for multi-save flows.
/// <para>
/// Opt <em>out</em>, not in: anything that is not an <see cref="IQuery{TResponse}"/> is treated as
/// a mutation and gets a transaction. A command that forgot an opt-in marker would lose atomicity
/// silently, which is the wrong direction to fail.
/// </para>
/// <para>
/// <strong>Registered innermost, after <c>OtpVerificationBehavior</c>.</strong> Anything that
/// performs an external side effect after a save — texting a code, sending mail, calling a payment
/// provider — must sit <em>outside</em> this behaviour. <c>OtpService.IssueAsync</c> is the live
/// example: it saves the challenge and then sends the SMS, relying on the save having committed.
/// Enclosed in a transaction, the <c>OtpRequiredException</c> that follows rolls the challenge
/// back while the message is already gone, and the code the user receives can never be verified.
/// </para>
/// </summary>
// "notnull" rather than "TRequest : IRequest<TResponse>", for the reason spelled out on
// ValidationBehavior: MediatR.Contracts 2.x made IRequest and IRequest<T> unrelated, so a void
// command is not an IRequest<Unit>, the tighter constraint cannot be satisfied, and the container
// skips the registration in silence — leaving exactly the mutating commands that need a
// transaction without one.
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
        if (request is IQuery<TResponse> || _dbContext.HasActiveTransaction)
            return await next();

        var response = default(TResponse);

        // Inert today: UseSqlServer is configured without EnableRetryOnFailure, so this is a
        // non-retrying strategy. Do not turn retries on without revisiting — ExecuteAsync replays
        // the whole delegate, next() included, so a transient failure would run the handler twice.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.BeginTransactionAsync();

            using (_logger.BeginScope(new List<KeyValuePair<string, object>> { new("TransactionContext", transaction!.TransactionId) }))
            {
                try
                {
                    response = await next();

                    await _dbContext.CommitTransactionAsync(transaction, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Disposing the transaction would roll the database back on its own, but it
                    // leaves ApplicationDbContext._currentTransaction pointing at a dead object,
                    // so HasActiveTransaction stays true and the next Send in this scope would
                    // quietly run with no transaction at all.
                    _dbContext.RollbackTransaction();

                    // The request itself is never logged: it carries OtpCode and Password on some
                    // commands, and Serilog destructuring does not go through LogRedactor.
                    _logger.LogError(ex, "Rolled back transaction for {CommandName}", request.GetGenericTypeName());

                    throw;
                }
            }
        });

        return response!;
    }
}
