using System.Text.RegularExpressions;

namespace WebApi.Routing
{
    /// <summary>
    /// Turns the <c>[controller]</c> and <c>[action]</c> route tokens into lowercase kebab-case
    /// segments, so <c>LoanApplicationsController</c> routes at <c>loan-applications</c> and
    /// <c>ResendOtp</c> at <c>resend-otp</c>.
    /// </summary>
    /// <remarks>
    /// Registered once via <c>RouteTokenTransformerConvention</c> in <c>AddWebUIServices</c>, which
    /// is what makes the casing a property of the API rather than something each controller has to
    /// remember. A new controller gets it for free; there is no route string to keep in sync with
    /// the class name.
    /// <para>
    /// <strong>Only tokens are transformed, not literal segments.</strong> The convention rewrites
    /// <c>[controller]</c>/<c>[action]</c>/<c>[area]</c> and nothing else; a literal like
    /// <c>[HttpPost("resend-otp")]</c> is passed through verbatim and would have to be spelled in
    /// kebab-case by hand. The RPC-style controllers use <c>[HttpPost("[action]")]</c> rather than
    /// a literal so that casing is decided here and nowhere else -- at the cost of coupling the
    /// public URL to the C# method name, so renaming such an action is an API change.
    /// </para>
    /// </remarks>
    public partial class SlugifyParameterTransformer : IOutboundParameterTransformer
    {
        public string? TransformOutbound(object? value)
        {
            if (value is null)
            {
                return null;
            }

            return CamelCaseBoundary().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();
        }

        [GeneratedRegex("([a-z0-9])([A-Z])")]
        private static partial Regex CamelCaseBoundary();
    }
}
