using Domain.Common;
using Domain.Enums;
using Domain.Events;
using Domain.Exceptions;

namespace Domain.Entities
{
    /// <summary>
    /// A command captured at initiate and held until someone confirms it. The server owns the
    /// payload for the whole of that window, which is the difference between this and the inline
    /// <c>IRequireOtpVerification</c> gate: there the client keeps the payload and replays it, so
    /// the confirmation can only ever come from whoever composed the request.
    /// <para>
    /// Holding it here is what makes an approval by a *different* user, on a different device, or
    /// hours later, expressible at all.
    /// </para>
    /// </summary>
    public class PendingOperation : BaseAuditableEntity, IAggregateRoot
    {
        /// <summary>
        /// The handle handed to the client. <see cref="BaseEntity.Id"/> is a sequential int and
        /// would let a caller walk other people's operations, so it never leaves the server —
        /// the same reasoning as <see cref="OtpVerification.ChallengeId"/>.
        /// </summary>
        public Guid OperationId { get; private set; }

        /// <summary>
        /// Stable discriminator from the operation registry, never a CLR type name. A type name
        /// would tie every parked row to the current namespace, and this is boilerplate other
        /// people fork and rename.
        /// </summary>
        public string? OperationType { get; private set; }

        /// <summary>
        /// The serialized command. Nothing sensitive may be captured here — the registry refuses
        /// to register a command carrying a secret, which is where that is enforced.
        /// </summary>
        public string? Payload { get; private set; }

        /// <summary>
        /// Who initiated. Distinct from <see cref="BaseAuditableEntity.CreatedBy"/>, which the
        /// audit interceptor stamps: this one is domain data that the confirmation policy reads,
        /// and it must not change meaning if auditing is ever reconfigured.
        /// </summary>
        public string? InitiatedBy { get; private set; }

        /// <summary>
        /// Independent of, and normally far longer than, the lifetime of any one code issued
        /// against this operation. One operation outlives many challenges.
        /// </summary>
        public DateTime ExpiresAt { get; private set; }

        public PendingOperationStatus Status { get; private set; }

        public string? ConfirmedBy { get; private set; }

        public DateTime? ConfirmedAt { get; private set; }

        /// <summary>
        /// Optimistic concurrency token. Two concurrent confirms make the second SaveChanges throw
        /// DbUpdateConcurrencyException instead of both executing the captured command.
        /// </summary>
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        /// <remarks>
        /// <paramref name="operationId"/> is supplied rather than generated here because the code
        /// hash that binds a challenge to this operation is keyed by it, so the caller has to know
        /// it before it can hash — as with <see cref="OtpVerification.Create"/>.
        /// </remarks>
        public static PendingOperation Create(
            Guid operationId,
            string operationType,
            string payload,
            string initiatedBy,
            DateTime expiresAt,
            DateTime now)
        {
            if (operationId == Guid.Empty)
                throw new DomainValidationException("PendingOperationNotFound");

            if (string.IsNullOrWhiteSpace(operationType))
                throw new DomainValidationException("InvalidOperationType");

            if (string.IsNullOrWhiteSpace(payload))
                throw new DomainValidationException("InvalidOperationPayload");

            if (string.IsNullOrWhiteSpace(initiatedBy))
                throw new DomainValidationException("InvalidOperationInitiator");

            if (expiresAt <= now)
                throw new DomainValidationException("InvalidPendingOperationLifetime");

            var entity = new PendingOperation()
            {
                OperationId = operationId,
                OperationType = operationType,
                Payload = payload,
                InitiatedBy = initiatedBy,
                ExpiresAt = expiresAt,
                Status = PendingOperationStatus.Pending
            };

            entity.AddDomainEvent(new PendingOperationCreatedEvent(entity));

            return entity;
        }

        /// <summary>
        /// Marks the operation spent. Whether <paramref name="confirmedBy"/> was *allowed* to
        /// confirm is decided outside — it depends on the policy declared on the command type,
        /// which the domain has no way to see. This records who did, and refuses a second go.
        /// </summary>
        public void Confirm(string confirmedBy, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(confirmedBy))
                throw new DomainValidationException("InvalidUser");

            if (Status != PendingOperationStatus.Pending)
                throw new DomainValidationException("PendingOperationAlreadyResolved");

            if (now >= ExpiresAt)
            {
                Status = PendingOperationStatus.Expired;
                throw new DomainValidationException("PendingOperationExpired");
            }

            Status = PendingOperationStatus.Confirmed;
            ConfirmedBy = confirmedBy;
            ConfirmedAt = now;
            LastModified = now;

            this.AddDomainEvent(new PendingOperationConfirmedEvent(this));
        }

        /// <summary>
        /// Abandons a still pending operation. Silent on an already resolved one, matching
        /// <see cref="OtpVerification.Invalidate"/> — cancelling twice is not an error worth
        /// surfacing to a caller who got the outcome they asked for.
        /// </summary>
        public void Cancel(DateTime now)
        {
            if (Status != PendingOperationStatus.Pending)
                return;

            Status = PendingOperationStatus.Cancelled;
            LastModified = now;

            this.AddDomainEvent(new PendingOperationCancelledEvent(this));
        }
    }
}
