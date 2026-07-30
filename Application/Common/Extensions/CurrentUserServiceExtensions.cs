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
