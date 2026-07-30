namespace Application.Common.Interfaces
{
    /// <summary>
    /// Turns a refresh token into the value stored on the row. Deterministic, because the stored
    /// hash <em>is</em> the lookup key — a salted-per-row scheme would force a table scan and an
    /// in-memory comparison of every candidate.
    /// </summary>
    public interface IRefreshTokenHasher
    {
        string Hash(string token);
    }
}
