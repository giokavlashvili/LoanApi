namespace Application.Common.Interfaces;

/// <summary>
/// Excludes a command from <see cref="Behaviors.TransactionBehavior{TRequest, TResponse}"/>.
/// <para>
/// The only valid reason to use it: the command performs an <strong>external side effect after
/// saving</strong> — texting a code, sending mail, calling a payment provider — and so needs its
/// save committed before that effect happens. <c>ResendOtpCommand</c> is the live example. It
/// persists a challenge and then sends the SMS; inside a transaction that save has not committed
/// yet, so a commit failure afterwards rolls the challenge back while the message is already gone
/// and the code the user received can never be verified.
/// </para>
/// <para>
/// <strong>Not</strong> for commands that are merely slow, or that "do not need" atomicity. A
/// single <c>SaveChanges</c> is already atomic, so exempting a one-save command costs nothing —
/// but the marker is a standing claim that a partial write is acceptable here, and that is almost
/// never true. If a command saves twice, it wants the transaction.
/// </para>
/// <para>
/// Sits on the command rather than the handler because a pipeline behaviour sees the request;
/// reaching the handler would take reflection. Same placement as <see cref="IQuery{TResponse}"/>
/// and <c>IRequireOtpVerification</c>.
/// </para>
/// </summary>
public interface ISkipTransaction
{
}
