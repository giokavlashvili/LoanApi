namespace Application.Common.Models
{
    /// <summary>
    /// A freshly issued refresh token, in the only form the caller can ever see it: the raw value
    /// exists here and nowhere else, because the row holds a hash.
    /// </summary>
    /// <param name="UserId">
    /// Carried out of a rotation so the caller can mint an access token without a second lookup of
    /// the token it just spent.
    /// </param>
    public record RefreshTokenIssue(string UserId, string Token, DateTime ExpiresAt);
}
