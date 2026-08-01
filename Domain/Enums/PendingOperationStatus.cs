namespace Domain.Enums
{
    /// <summary>
    /// The whole state machine, deliberately.
    /// <para>
    /// There is no <c>Executing</c> status. Its only job would be blocking two concurrent
    /// confirms, and <see cref="Entities.OtpVerification"/>'s <c>RowVersion</c> already does that:
    /// of two racing confirms the second <c>SaveChanges</c> throws
    /// <c>DbUpdateConcurrencyException</c>, which the API maps to 409. Adding a lease would bring
    /// a timeout to tune, a release path, and a reaper — all to manage a stuck state that does not
    /// otherwise exist.
    /// </para>
    /// </summary>
    public enum PendingOperationStatus
    {
        Pending = 0,
        Succeeded,
        Failed
    }
}
