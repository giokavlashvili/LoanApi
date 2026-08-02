using Microsoft.AspNetCore.Mvc;

namespace WebApi.Models
{
    /// <summary>
    /// The 428 body returned when an operation needs phone confirmation. Carries the four values a
    /// client needs to complete the flow: which challenge to answer, how long it lives, which number
    /// was texted, and how many wrong guesses remain.
    /// <para>
    /// These four used to be written to <see cref="ProblemDetails.Extensions"/>. The wire format is
    /// identical either way — <c>Extensions</c> is <c>[JsonExtensionData]</c>, so its entries
    /// serialize flat alongside <c>type</c>/<c>title</c>/<c>status</c>/<c>detail</c> exactly as
    /// declared properties do — but an extension dictionary is invisible to the OpenAPI generator.
    /// Declared properties are not, so Swagger, Scalar and the generated TypeScript client all
    /// describe the response a caller actually receives.
    /// </para>
    /// <para>
    /// Returned only by <c>ApiExceptionFilterAttribute.HandleOtpRequiredException</c>, which is what
    /// keeps this type and the response it documents from drifting apart.
    /// </para>
    /// </summary>
    public class OtpChallengeProblemDetails : ProblemDetails
    {
        /// <summary>The challenge to answer. Send it back as <c>challengeId</c> on the retried request.</summary>
        public Guid ChallengeId { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Masked, e.g. <c>*********3456</c>. Enough for the user to recognise which of their
        /// numbers was texted, not enough to disclose it to someone who does not already know it.
        /// </summary>
        public string? Recipient { get; set; }

        /// <summary>Wrong guesses allowed before the challenge locks.</summary>
        public int MaxAttempts { get; set; }
    }
}
