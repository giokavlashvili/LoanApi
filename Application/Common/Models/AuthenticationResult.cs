namespace Application.Common.Models
{
    /// <summary>
    /// The pair handed back by a successful login or refresh. A named type rather than the tuple
    /// this replaced: four values, two of them <see cref="DateTime"/> and two of them
    /// <see cref="string"/>, is exactly the shape where positional element names stop being enough.
    /// </summary>
    public record AuthenticationResult(
        string AccessToken,
        DateTime ValidTo,
        string RefreshToken,
        DateTime RefreshTokenValidTo);
}
