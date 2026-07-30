namespace Application.Authenticate.Dtos
{
    /// <summary>
    /// Returned by both login and refresh: the two endpoints hand back the same thing, so they
    /// share one contract and the generated client gets one type instead of two identical ones.
    /// </summary>
    public class TokenPairDto
    {
        public string? AccessToken { get; set; }

        public DateTime ValidTo { get; set; }

        /// <summary>
        /// Opaque, and single use. Every successful refresh invalidates it and returns a
        /// replacement, so a client must store whatever came back rather than keeping the original.
        /// </summary>
        public string? RefreshToken { get; set; }

        public DateTime RefreshTokenValidTo { get; set; }
    }
}
