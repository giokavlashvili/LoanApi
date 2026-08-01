namespace Application.Common.Interfaces;

/// <summary>
/// Excludes a command from <see cref="Behaviors.TransactionBehavior{TRequest, TResponse}"/>.
/// <para>
/// There are exactly two valid reasons, and both are about the <em>boundary</em> being wrong —
/// never about atomicity being unwanted.
/// </para>
/// <para>
/// <strong>1. An external side effect after saving</strong> — texting a code, sending mail,
/// calling a payment provider — where the save must be committed before the effect happens.
/// <c>ResendOtpCommand</c> and <c>InitiateOperationCommand</c> are the live examples: both persist
/// a challenge and then send the SMS, and inside a transaction that save has not committed yet, so
/// a commit failure afterwards rolls the challenge back while the message is already gone and the
/// code the user received can never be verified.
/// </para>
/// <para>
/// <strong>2. The command manages a narrower boundary itself.</strong>
/// <c>ConfirmOperationCommand</c> is the live example: the automatic transaction would start
/// before code verification, and <c>OtpService.VerifyAsync</c> persists its <c>AttemptCount</c>
/// increment in a <c>finally</c> precisely so a wrong code always counts. Rolled back, the attempt
/// limit is never reached and six digits become brute-forceable. It verifies first, then opens its
/// own transaction around execution and the result write.
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
