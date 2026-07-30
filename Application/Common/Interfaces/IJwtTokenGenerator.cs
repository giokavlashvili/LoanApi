namespace Application.Common.Interfaces
{
    /// <summary>
    /// Mints a signed access token for an already authenticated user.
    /// <para>
    /// Deciding <em>whether</em> to issue a token is a use case concern and stays with the caller;
    /// signing one is mechanism — a key, an algorithm and a clock. Nothing here checks a password,
    /// so calling it is an assertion that the credentials were already verified.
    /// </para>
    /// </summary>
    public interface IJwtTokenGenerator
    {
        (string Token, DateTime ValidTo) Generate(string userId, string userName, IEnumerable<string> roles);
    }
}
