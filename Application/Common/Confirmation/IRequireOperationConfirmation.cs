namespace Application.Common.Confirmation
{
    /// <summary>
    /// Marks a command that must be captured at initiate and executed only once someone confirms
    /// it. <see cref="Behaviors.OperationConfirmationBehavior{TRequest, TResponse}"/> does the
    /// rest: a request arriving normally is parked and rejected, and the captured payload is
    /// replayed later by <c>ConfirmOperationCommandHandler</c>.
    /// <para>
    /// Pair it with <see cref="ConfirmableOperationAttribute"/>, which carries the discriminator
    /// and the policy. The registry refuses to start if one is present without the other.
    /// </para>
    /// <para>
    /// <b>Deliberately empty, and it must stay that way.</b> The obvious design puts a
    /// <c>Guid? OperationId</c> here and has the behaviour read "null means initiate, set means
    /// already confirmed". Commands are model bound straight from the request body, so that
    /// property is caller controlled: anyone could post the ordinary endpoint with an arbitrary
    /// operation id and the behaviour would wave the command through with no pending operation,
    /// no code, and no confirmation of any kind. The confirmed-execution signal therefore travels
    /// out of band, in <see cref="IOperationExecutionContext"/>, where nothing on the wire can
    /// reach it.
    /// </para>
    /// </summary>
    public interface IRequireOperationConfirmation
    {
    }
}
