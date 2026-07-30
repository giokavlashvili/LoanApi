using Application.Common.Interfaces;
using Domain.Exceptions;

namespace Application.Common.Extensions
{
    public static class CurrentUserServiceExtensions
    {
        /// <summary>
        /// The authenticated caller's id, or <c>DomainValidationException("InvalidUser")</c> if
        /// there is not one.
        /// <para>
        /// This invariant used to live inside <c>LoanApplication</c>, which could enforce it because
        /// every factory and mutator was handed a user id. Now that
        /// <c>AuditableEntityInterceptor</c> stamps the audit columns, the entity never sees one —
        /// so the check moved here rather than being dropped. Without it a request with no
        /// authenticated user would persist a row with a null <c>CreatedBy</c> and no complaint.
        /// </para>
        /// <para>
        /// Callers that only need the guard may discard the return value; it is returned as a
        /// non-null <see cref="string"/> so callers that do need the id get one without a null
        /// check of their own.
        /// </para>
        /// <para>
        /// <b>Expected never to throw in production — do not delete it as dead code.</b> Every
        /// command that calls it sits behind an <c>[Authorize]</c> controller, and
        /// <c>JwtTokenGenerator</c> always emits <c>ClaimTypes.NameIdentifier</c>, so an
        /// authenticated request always has an id. It earns its place as the last guard on a real
        /// database constraint: <c>LoanApplicationConfiguration</c> marks <c>CreatedBy</c>
        /// <c>IsRequired()</c>, so if the id were ever absent the insert would fail deep in
        /// <c>SaveChanges</c> as a <c>NOT NULL</c> violation and surface as a 500. This turns that
        /// into a localized 400 at the top of the handler. Anything that removes the
        /// <c>[Authorize]</c> attribute, or issues a token without that claim, makes it live.
        /// </para>
        /// </summary>
        public static string RequireUserId(this ICurrentUserService currentUserService)
        {
            var userId = currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                throw new DomainValidationException("InvalidUser");

            return userId;
        }
    }
}
