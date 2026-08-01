using Domain.Common;
using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    /// <summary>
    /// A request captured at <c>initiate</c>, waiting for the code that releases it, plus the
    /// outcome once it has run. The challenge that gates it lives in
    /// <see cref="OtpVerification"/>; this owns only the payload and what became of it.
    /// <para>
    /// The payload is stored as plain JSON. That is a deliberate trade recorded in phase 6 —
    /// database access is controlled at the infrastructure level, and anything genuinely sensitive
    /// is expected to use <c>IRequireOtpVerification</c> instead, which never persists a payload.
    /// </para>
    /// </summary>
    public class PendingOperation : BaseAuditableEntity, IAggregateRoot
    {
        /// <summary>
        /// The handle handed to the client. <see cref="BaseEntity.Id"/> is a sequential int and
        /// would let a caller walk other people's operations, so it never leaves the server.
        /// </summary>
        public Guid OperationId { get; private set; }

        /// <summary>
        /// The challenge currently gating this operation. Not readonly: a resend issues a
        /// <em>new</em> challenge and retires the old one, so without moving this the next confirm
        /// would verify against an invalidated challenge and fail forever.
        /// See <see cref="Rechallenge"/>.
        /// </summary>
        public Guid ChallengeId { get; private set; }

        /// <summary>Registry key, and the OTP purpose the challenge is scoped to.</summary>
        public string? OperationType { get; private set; }

        public string? Payload { get; private set; }

        /// <summary>Serialized result, kept so a retry after a lost response replays it rather than re-running.</summary>
        public string? ResultPayload { get; private set; }

        /// <summary>Localization key describing why it failed.</summary>
        public string? ErrorKey { get; private set; }

        /// <summary>Principal that initiated. A different one at confirm is treated as not found.</summary>
        public string? UserId { get; private set; }

        public DateTime? ExecutedAt { get; private set; }

        public PendingOperationStatus Status { get; private set; }

        /// <summary>
        /// Conventional here — <see cref="OtpVerification"/> and <see cref="RefreshToken"/> both
        /// carry one. Not load-bearing: concurrent confirms are already settled on the challenge,
        /// which is why this aggregate needs no <c>Executing</c> lease.
        /// </summary>
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        public static PendingOperation Create(
            Guid operationId,
            Guid challengeId,
            string operationType,
            string payload,
            string? userId,
            DateTime created)
        {
            if (operationId == Guid.Empty)
                throw new DomainValidationException("PendingOperationNotFound");

            if (challengeId == Guid.Empty)
                throw new DomainValidationException("OtpChallengeNotFound");

            if (string.IsNullOrWhiteSpace(operationType))
                throw new DomainValidationException("UnknownVerifiableOperation");

            if (string.IsNullOrWhiteSpace(payload))
                throw new DomainValidationException("PendingOperationUnavailable");

            return new PendingOperation
            {
                OperationId = operationId,
                ChallengeId = challengeId,
                OperationType = operationType,
                Payload = payload,
                UserId = userId,
                Status = PendingOperationStatus.Pending,
                Created = created,
                CreatedBy = userId
            };
        }

        /// <summary>
        /// Points the operation at a freshly issued challenge after a resend. Only meaningful
        /// while still pending — a terminal operation has nothing left to confirm.
        /// </summary>
        public void Rechallenge(Guid challengeId, DateTime now)
        {
            if (challengeId == Guid.Empty)
                throw new DomainValidationException("OtpChallengeNotFound");

            if (Status != PendingOperationStatus.Pending)
                throw new DomainValidationException("PendingOperationAlreadyCompleted");

            ChallengeId = challengeId;
            LastModified = now;
        }

        /// <summary>
        /// Records the outcome and drops the payload — it has served its purpose, and the row is
        /// still wanted for audit.
        /// </summary>
        public void Succeed(string? resultPayload, DateTime now)
        {
            EnsurePending();

            Status = PendingOperationStatus.Succeeded;
            ResultPayload = resultPayload;
            Payload = null;
            ExecutedAt = now;
            LastModified = now;
        }

        public void Fail(string errorKey, DateTime now)
        {
            EnsurePending();

            if (string.IsNullOrWhiteSpace(errorKey))
                throw new DomainValidationException("PendingOperationUnavailable");

            Status = PendingOperationStatus.Failed;
            ErrorKey = errorKey;
            Payload = null;
            ExecutedAt = now;
            LastModified = now;
        }

        private void EnsurePending()
        {
            if (Status != PendingOperationStatus.Pending)
                throw new DomainValidationException("PendingOperationAlreadyCompleted");
        }
    }
}
